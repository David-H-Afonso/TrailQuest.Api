using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Application;

public sealed class CommunityService(TrailQuestDbContext db, CurrentUser current, HealthService health,
    AdventureService adventures, DemoData demo, TimeProvider clock, LocalMutationGate gate)
{
    public async Task<CommunityDto> ReadAsync(string profile, CancellationToken ct)
    {
        await health.ProfileAsync(profile, ct);
        var id = current.Id;
        if (profile == "demo") return new([], demo.Posts(id, clock.GetUtcNow()));
        var relationships = await db.Friendships.AsNoTracking().Where(x => x.RequesterId == id || x.RecipientId == id).ToListAsync(ct);
        var blocked = await db.Blocks.AsNoTracking().Where(x => x.UserId == id || x.BlockedUserId == id).ToListAsync(ct);
        var blockedIds = blocked.Select(x => x.UserId == id ? x.BlockedUserId : x.UserId).ToHashSet();
        var ids = relationships.Select(x => x.RequesterId == id ? x.RecipientId : x.RequesterId).Where(x => !blockedIds.Contains(x)).ToList();
        var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id) || x.Id == id).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var friends = relationships.Where(x => !blockedIds.Contains(x.RequesterId == id ? x.RecipientId : x.RequesterId))
            .Select(x => new FriendDto(x.Id, x.RequesterId == id ? x.RecipientId : x.RequesterId,
                names.GetValueOrDefault(x.RequesterId == id ? x.RecipientId : x.RequesterId, "Usuario"), x.Status, x.RecipientId == id)).ToList();
        var visible = friends.Where(x => x.Status == "accepted").Select(x => x.UserId).Append(id).ToList();
        // Server-created UTC timestamps sort consistently as SQLite text. Bound in SQL,
        // rather than loading every snapshot/track to sort DateTimeOffset in memory.
        var visibleJson = JsonSerializer.Serialize(visible);
        var posts = await db.Posts.FromSqlInterpolated($"SELECT * FROM shared_posts WHERE UserId IN (SELECT value FROM json_each({visibleJson})) ORDER BY CreatedAtUtc DESC, Id LIMIT 100").AsNoTracking().ToListAsync(ct);
        return new(friends, posts.Select(x => new PostDto(x.Id, x.UserId,
            names.GetValueOrDefault(x.UserId, "Usuario"), x.Kind, x.Title, x.Summary, x.CreatedAtUtc, x.UserId == id,
            JsonSerializer.Deserialize<List<TrackPointDto>>(x.TrackJson, RouteCatalog.Json) ?? [])).ToList());
    }
    public async Task RequestAsync(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 32) throw new AppException("invalid_friend", 400, "Use an exact account username.");
        var id = current.Id; var normalized = username.Trim().ToUpperInvariant();
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var other = await db.Users.SingleOrDefaultAsync(x => x.NormalizedUsername == normalized, ct);
            if (other is null || other.Id == id || await db.Blocks.AnyAsync(x => (x.UserId == id && x.BlockedUserId == other.Id) || (x.UserId == other.Id && x.BlockedUserId == id), ct))
                throw new AppException("friend_unavailable", 404, "This friendship request is not available.");
            if (await db.Friendships.AnyAsync(x => (x.RequesterId == id && x.RecipientId == other.Id) || (x.RequesterId == other.Id && x.RecipientId == id), ct))
                throw new AppException("friend_exists", 409, "A relationship or request already exists.");
            db.Friendships.Add(new() { RequesterId = id, RecipientId = other.Id, CreatedAtUtc = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }
    public async Task RespondAsync(string relationshipId, bool accept, CancellationToken ct)
    {
        var id = current.Id;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await db.Friendships.SingleOrDefaultAsync(x => x.Id == relationshipId && (x.RequesterId == id || x.RecipientId == id), ct)
                ?? throw new AppException("friend_not_found", 404, "Relationship not found.");
            if (accept)
            {
                if (row.RecipientId != id || row.Status != "pending") throw new AppException("friend_forbidden", 403, "Only the recipient can accept a pending request.");
                row.Status = "accepted";
            }
            else db.Friendships.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }
    public async Task BlockAsync(string userId, CancellationToken ct)
    {
        var id = current.Id;
        if (userId == id || !await db.Users.AnyAsync(x => x.Id == userId, ct)) throw new AppException("friend_not_found", 404, "User not found.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            if (!await db.Blocks.AnyAsync(x => x.UserId == id && x.BlockedUserId == userId, ct)) db.Blocks.Add(new() { UserId = id, BlockedUserId = userId });
            await db.Friendships.Where(x => (x.RequesterId == id && x.RecipientId == userId) || (x.RequesterId == userId && x.RecipientId == id)).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }
    public async Task<PostDto> PreviewAsync(ShareRequest request, CancellationToken ct)
    {
        var id = current.Id;
        await health.ProfileAsync(request.ProfileId, ct);
        string title, summary;
        IReadOnlyList<TrackPointDto> track = [];
        if (request.Kind == "activity" && long.TryParse(request.SourceId, out var activityId))
        {
            var activity = await health.DetailAsync(request.ProfileId, activityId, ct);
            title = activity.Activity.Name;
            summary = activity.Activity.DurationSeconds is { } duration ? $"Actividad completada · {Math.Round(duration / 60)} min" : "Actividad completada · duración no registrada";
            if (activity.Activity.DistanceMeters is { } meters) summary += $" · {meters / 1000:0.##} km";
            if (request.IncludeTrack) track = CropTrack(activity.Track);
        }
        else if (request.Kind == "challenge")
        {
            var challenge = (await health.ChallengesAsync(request.ProfileId, ct)).SingleOrDefault(x => x.Id == request.SourceId)
                ?? throw new AppException("challenge_not_found", 404, "Challenge not found.");
            if (!challenge.Completed) throw new AppException("challenge_incomplete", 409, "Complete this challenge before sharing it.");
            title = challenge.Name;
            summary = $"Reto alcanzado · {challenge.Value:0.##} {challenge.Unit} registrados. {challenge.ComparisonNote}";
        }
        else if (request.Kind is "journey" or "checkpoint")
        {
            var dashboard = await adventures.GetDashboardAsync(request.ProfileId, ct);
            var journey = dashboard.Journeys.SingleOrDefault(x => x.RouteId == request.SourceId)
                ?? throw new AppException("journey_not_found", 404, "Journey not found.");
            var route = journey.Definition!;
            if (request.Kind == "journey")
            {
                if (journey.ProgressPercent < 100) throw new AppException("journey_incomplete", 409, "Complete this adventure before sharing its completion.");
                title = route.Passport.Title; summary = $"He completado la ruta virtual {route.Name} · {route.DistanceKm:0.##} km.";
            }
            else
            {
                var checkpoint = route.Checkpoints.SingleOrDefault(x => x.Id == request.CheckpointId && journey.Visits.Any(v => v.CheckpointId == x.Id))
                    ?? throw new AppException("checkpoint_locked", 404, "This stamp has not been earned.");
                title = checkpoint.Name;
                summary = checkpoint.DistanceKm == 0 ? $"He empezado {route.Name}. Sello de salida: {checkpoint.Name}." : $"He conseguido el sello de {checkpoint.Name} al completar {checkpoint.DistanceKm:0.##} km en {route.Name}.";
            }
        }
        else throw new AppException("invalid_share", 400, "Select an earned stamp, completed passport or your own activity.");
        var name = await db.Users.Where(x => x.Id == id).Select(x => x.DisplayName).SingleAsync(ct);
        return new("", id, name, request.Kind, title, summary, clock.GetUtcNow(), true, track);
    }
    public static IReadOnlyList<TrackPointDto> CropTrack(IReadOnlyList<TrackPointDto> points)
    {
        if (points.Count < 2) return [];
        return points.Where(x => ActivityFileReader.Distance(x, points[0]) > 500 && ActivityFileReader.Distance(x, points[^1]) > 500)
            .Select(x => x with { AtUtc = null }).ToList();
    }
    public async Task<PostDto> PublishAsync(ShareRequest request, CancellationToken ct)
    {
        if (request.ProfileId != "personal") throw new AppException("demo_preview_only", 400, "Demo publications are previews only and are not shared with real accounts.");
        var preview = await PreviewAsync(request, ct);
        var row = new SharedPost { UserId = current.Id, Kind = preview.Kind, Title = preview.Title,
            Summary = preview.Summary, TrackJson = JsonSerializer.Serialize(preview.Track, RouteCatalog.Json), CreatedAtUtc = clock.GetUtcNow() };
        await gate.Semaphore.WaitAsync(ct);
        try { db.Posts.Add(row); await db.SaveChangesAsync(ct); }
        finally { gate.Semaphore.Release(); }
        return preview with { Id = row.Id };
    }
    public async Task DeleteAsync(string postId, CancellationToken ct)
    {
        var id = current.Id;
        if (await db.Posts.Where(x => x.Id == postId && x.UserId == id).ExecuteDeleteAsync(ct) == 0)
            throw new AppException("post_not_found", 404, "Publication not found.");
    }
}
