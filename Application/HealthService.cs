using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Application;

public sealed class HealthService(TrailQuestDbContext db, CurrentUser current, TimeProvider clock, LocalMutationGate gate, IWebHostEnvironment environment)
{
    public async Task<ProfileSettings> ProfileAsync(string kind, CancellationToken ct)
    {
        if (kind is not ("demo" or "personal")) throw new AppException("profile_not_found", 404, "Profile not found.");
        var id = current.Id;
        return await db.Profiles.SingleOrDefaultAsync(x => x.UserId == id && x.Kind == kind, ct)
            ?? throw new AppException("profile_not_found", 404, "Profile not found.");
    }
    public DateOnly Today(ProfileSettings profile) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId)).DateTime);
    public static void ValidateRange(DateOnly start, DateOnly end, int maxDays = 366)
    {
        if (start < new DateOnly(2000, 1, 1) || end < start || end.DayNumber - start.DayNumber >= maxDays)
            throw new AppException("invalid_range", 400, $"Select an inclusive range of at most {maxDays} days.");
    }
    public IQueryable<ActivityRecord> ActivityQuery(string profileId, bool includeTrack = false) => db.Activities.AsNoTracking().Where(x => x.ProfileId == profileId).Select(x => new ActivityRecord
    {
        Id = x.Id, ProfileId = x.ProfileId, SourceId = x.SourceId, Source = x.Source, Name = x.Name, Sport = x.Sport,
        Date = x.Date, StartedAtUtc = x.StartedAtUtc, TimeZoneId = x.TimeZoneId, DurationSeconds = x.DurationSeconds,
        DistanceMeters = x.DistanceMeters, CaloriesKcal = x.CaloriesKcal, AverageHeartRate = x.AverageHeartRate,
        Repetitions = x.Repetitions, Sets = x.Sets, VolumeKg = x.VolumeKg, ExercisesJson = x.ExercisesJson,
        TrackJson = includeTrack ? x.TrackJson : "[]", TrackPointCount = x.TrackPointCount,
        OriginalStatus = x.OriginalStatus, UpdatedAtUtc = x.UpdatedAtUtc
    });
    public static ActivityDto ToDto(ActivityRecord item) => new(item.Id, item.Name, item.Sport, item.Source, item.StartedAtUtc,
        item.DurationSeconds, item.DistanceMeters, item.CaloriesKcal, item.AverageHeartRate, item.Repetitions, item.Sets, item.VolumeKg,
        JsonSerializer.Deserialize<List<ExerciseDto>>(item.ExercisesJson, RouteCatalog.Json) ?? [], item.OriginalStatus,
        item.TrackPointCount);

    public async Task<HealthOverviewDto> OverviewAsync(string kind, DateOnly? from, DateOnly? until, CancellationToken ct)
    {
        var profile = await ProfileAsync(kind, ct);
        var end = until ?? Today(profile); var start = from ?? end.AddDays(-27);
        ValidateRange(start, end);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var documentQuery = db.HealthDocuments.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Date >= start && x.Date <= end);
        var activityQuery = db.Activities.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Date >= start && x.Date <= end);
        await ArchiveReadBudget.EnsureAsync(documentQuery, activityQuery, false, ct);
        var activities = await ActivityQuery(profile.Id).Where(x => x.Date >= start && x.Date <= end).ToListAsync(ct);
        var documents = await documentQuery.ToListAsync(ct);
        var days = documents.GroupBy(x => x.Date).OrderByDescending(x => x.Key).Select(group => new HealthDayDto(group.Key,
            group.OrderBy(x => x.RetrievedAtUtc).ThenBy(x => x.Id).SelectMany(HealthNormalizer.Metrics).GroupBy(x => x.Id).Select(x => x.Last()).ToList(),
            group.SelectMany(HealthNormalizer.Series).ToList())).ToList();
        var coverage = documents.GroupBy(x => x.Dataset).Select(x => new DatasetCoverageDto(x.Key, x.Count(), x.Max(y => y.RetrievedAtUtc))).ToList();
        return new(kind, start, end, activities.OrderByDescending(x => x.StartedAtUtc).Select(ToDto).ToList(), days, coverage, profile.LastGarminSyncAtUtc);
    }
    public async Task<ActivityDetailDto> DetailAsync(string kind, long id, CancellationToken ct)
    {
        var profile = await ProfileAsync(kind, ct);
        var activity = await ActivityQuery(profile.Id, includeTrack: true).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new AppException("activity_not_found", 404, "Activity not found.");
        return new(ToDto(activity), JsonSerializer.Deserialize<List<TrackPointDto>>(activity.TrackJson, RouteCatalog.Json) ?? []);
    }
    public async Task<IReadOnlyList<RawDocumentDto>> DocumentsAsync(string kind, string dataset, CancellationToken ct, int limit = 31)
    {
        var profile = await ProfileAsync(kind, ct);
        if (limit is < 1 or > 31) throw new AppException("invalid_limit", 400, "Select 1–31 source records.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var query = db.HealthDocuments.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Dataset == dataset).OrderByDescending(x => x.Date).Take(limit);
        await ArchiveReadBudget.EnsureAsync(query, db.Activities.Where(x => false), false, ct);
        var documents = await query.ToListAsync(ct);
        return documents.Select(x => new RawDocumentDto(x.Id, x.Dataset, x.Date, x.Source, x.RetrievedAtUtc, JsonSerializer.Deserialize<JsonElement>(x.PayloadJson))).ToList();
    }

    public async Task<GarminDataExportDto> ExportGarminDataAsync(string kind, DateOnly start, DateOnly end,
        IReadOnlyList<string>? requestedDatasets, CancellationToken ct)
    {
        var profile = await ProfileAsync(kind, ct);
        if (profile.Kind != "personal") throw new AppException("personal_profile_required", 400, "Garmin exports are only available for your personal profile.");
        ValidateRange(start, end);
        if (end > Today(profile)) throw new AppException("invalid_range", 400, "The export period cannot include future dates.");

        var requested = (requestedDatasets ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var known = GarminDatasets.All;
        IReadOnlyList<DatasetDto> selected;
        if (requested.Length == 1 && requested[0] == "all") selected = known;
        else
        {
            if (requested.Length == 0 || requested.Contains("all", StringComparer.Ordinal) || requested.Any(x => known.All(d => d.Id != x)))
                throw new AppException("invalid_dataset_selection", 400, "Select all datasets or one or more valid Garmin datasets.");
            var ids = requested.ToHashSet(StringComparer.Ordinal);
            selected = known.Where(x => ids.Contains(x.Id)).ToList();
        }

        // Garmin activity source records are exported as summaries below. Do not copy raw
        // activity detail documents here: provider payloads may contain location fields.
        var documentDatasets = selected.Where(x => x.Id != "activities")
            .SelectMany(x => new[] { x.Id, x.Id + "-envelope" }).Distinct(StringComparer.Ordinal).ToArray();
        var documentsQuery = db.HealthDocuments.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Source == "garmin" &&
            x.Date >= start && x.Date <= end && documentDatasets.Contains(x.Dataset));
        var includeSteps = selected.Any(x => x.Id == "steps");
        var stepsQuery = db.DailySteps.AsNoTracking().Where(x => includeSteps && x.ProfileId == profile.Id &&
            x.Source == "garmin" && x.Date >= start && x.Date <= end);
        var includeActivities = selected.Any(x => x.Id == "activities");
        var activitiesQuery = db.Activities.AsNoTracking().Where(x => includeActivities && x.ProfileId == profile.Id &&
            x.Source == "garmin" && x.Date >= start && x.Date <= end);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ArchiveReadBudget.EnsureAsync(documentsQuery, activitiesQuery, includeTracks: false, ct);
        var rawDocuments = await documentsQuery.OrderBy(x => x.Date).ThenBy(x => x.Dataset).ThenBy(x => x.Id)
            .Select(x => new { x.Date, x.Dataset, x.PayloadJson }).ToListAsync(ct);
        var steps = await stepsQuery.OrderBy(x => x.Date)
            .Select(x => new { x.Date, x.Steps, x.DistanceMeters, x.StepLengthCm, x.IsDistanceEstimated }).ToListAsync(ct);
        var rawActivities = await activitiesQuery.OrderBy(x => x.Date).ThenBy(x => x.SourceId)
            .Select(x => new { x.Date, x.Name, x.Sport, x.StartedAtUtc, x.DurationSeconds, x.DistanceMeters,
                x.CaloriesKcal, x.AverageHeartRate, x.Repetitions, x.Sets, x.VolumeKg, x.ExercisesJson }).ToListAsync(ct);

        var stepsByDate = steps.GroupBy(x => x.Date).ToDictionary(x => x.Key,
            x => (IReadOnlyList<GarminExportStepDto>)x.Select(day => new GarminExportStepDto(day.Steps,
                day.DistanceMeters, day.StepLengthCm, day.IsDistanceEstimated)).ToList());
        var activitiesByDate = rawActivities.GroupBy(x => x.Date).ToDictionary(x => x.Key,
            x => (IReadOnlyList<GarminExportActivityDto>)x.Select(activity => new GarminExportActivityDto(
                activity.Name, activity.Sport, activity.StartedAtUtc, activity.DurationSeconds, activity.DistanceMeters,
                activity.CaloriesKcal, activity.AverageHeartRate, activity.Repetitions, activity.Sets, activity.VolumeKg,
                JsonSerializer.Deserialize<List<ExerciseDto>>(activity.ExercisesJson, RouteCatalog.Json) ?? [])).ToList());
        var documentsByDate = rawDocuments.GroupBy(x => x.Date).ToDictionary(x => x.Key,
            x => (IReadOnlyList<GarminExportHealthRecordDto>)x.Select(document => new GarminExportHealthRecordDto(
                document.Dataset, JsonSerializer.Deserialize<JsonElement>(document.PayloadJson))).ToList());
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(offset =>
        {
            var date = start.AddDays(offset);
            return new GarminExportDayDto(date,
                stepsByDate.GetValueOrDefault(date, []), activitiesByDate.GetValueOrDefault(date, []),
                documentsByDate.GetValueOrDefault(date, []));
        }).ToList();

        return new GarminDataExportDto(1, clock.GetUtcNow(), kind, start, end, selected.Select(x => x.Id).ToArray(),
            selected.Where(x => x.Sensitive).Select(x => x.Id).ToArray(),
            new GarminExportPrivacyDto(IncludesCredentials: false, IncludesOriginalActivityFiles: false, IncludesGpsTracks: false), days);
    }
    public async Task<IReadOnlyList<ChallengeDto>> ChallengesAsync(string kind, CancellationToken ct)
    {
        var profile = await ProfileAsync(kind, ct);
        var strength = db.Activities.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Date <= Today(profile) && x.Sport.ToLower().Contains("strength"));
        var volume = await strength.SumAsync(x => x.VolumeKg ?? 0, ct);
        var repetitions = await strength.SumAsync(x => (double)(x.Repetitions ?? 0), ct);
        var sessions = await strength.CountAsync(ct);
        var path = Path.Combine(environment.ContentRootPath, "Content", "Challenges", "challenges.json");
        var definitions = JsonSerializer.Deserialize<List<ChallengeDefinition>>(await File.ReadAllTextAsync(path, ct), RouteCatalog.Json) ?? [];
        if (definitions.Any(x => !double.IsFinite(x.Target) || x.Target <= 0) || definitions.Select(x => x.Id).Distinct().Count() != definitions.Count)
            throw new InvalidOperationException("Challenge IDs must be unique and targets must be finite and positive.");
        return definitions.Select(x =>
        {
            var value = x.Metric switch { "strength-volume" => volume, "strength-repetitions" => repetitions, "strength-sessions" => sessions, _ => throw new InvalidOperationException("Unknown challenge metric.") };
            return new ChallengeDto(x.Id, x.Name, x.Description, x.Metric, x.Unit, x.Target, value,
                Math.Round(Math.Clamp(value / x.Target * 100, 0, 100), 1), value >= x.Target, x.ComparisonNote);
        }).ToList();
    }
    public async Task ImportDocumentAsync(ProfileSettings profile, string dataset, string key, DateOnly fallbackDate, JsonElement payload, CancellationToken ct)
    {
        var items = HealthNormalizer.Items(payload);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var dateText = HealthNormalizer.Text(item, "calendarDate", "date", "summaryDate");
            var date = DateOnly.TryParse(dateText, out var parsed) ? parsed : fallbackDate;
            var recordId = HealthNormalizer.Text(item, "samplePk", "uuid", "id", "activityId");
            var sourceKey = recordId is not null ? $"{dateText ?? key}/{recordId}" : dateText ?? (items.Count > 1 ? $"{key}/{index}" : key);
            var json = item.GetRawText(); var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var row = db.HealthDocuments.Local.FirstOrDefault(x => x.ProfileId == profile.Id && x.Dataset == dataset && x.SourceKey == sourceKey)
                ?? await db.HealthDocuments.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.Dataset == dataset && x.SourceKey == sourceKey, ct);
            if (row is null) { row = new() { ProfileId = profile.Id, Dataset = dataset, SourceKey = sourceKey }; db.HealthDocuments.Add(row); }
            row.Date = date; row.PayloadJson = json; row.ContentHash = hash; row.RetrievedAtUtc = clock.GetUtcNow();
        }
    }
    public async Task<long> ImportFileAsync(string kind, byte[] bytes, string name, CancellationToken ct)
    {
        var profile = await ProfileAsync(kind, ct);
        if (name.Length > 120 || name.Any(char.IsControl)) throw new AppException("invalid_name", 400, "Use a name of at most 120 characters.");
        var decoded = ActivityFileReader.Read(bytes);
        if (decoded.StartedAtUtc is null)
            throw new AppException("activity_time_missing", 400, "This file has no recorded activity time. Export an activity with timestamps; an untimed course is not a completed activity.");
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await db.Activities.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.OriginalHash == decoded.Hash, ct);
            if (row is not null) return row.Id;
            var started = decoded.StartedAtUtc.Value;
            row = new() { ProfileId = profile.Id, SourceId = decoded.Hash, Source = "file", Name = string.IsNullOrWhiteSpace(name) ? "Actividad importada" : name.Trim(),
                Sport = decoded.Sport?.Contains("Strength", StringComparison.OrdinalIgnoreCase) == true ? "strength_training" : decoded.Sport?.ToLowerInvariant() ?? "other",
                StartedAtUtc = started, Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(started, TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId)).DateTime),
                DurationSeconds = decoded.DurationSeconds, DistanceMeters = decoded.DistanceMeters, UpdatedAtUtc = clock.GetUtcNow() };
            ApplyFile(row, bytes, decoded); db.Activities.Add(row); await db.SaveChangesAsync(ct); return row.Id;
        }
        finally { gate.Semaphore.Release(); }
    }
    public static void ApplyFile(ActivityRecord row, byte[] bytes, DecodedActivity decoded)
    {
        row.OriginalFile = bytes; row.OriginalFormat = decoded.Format; row.OriginalHash = decoded.Hash;
        row.TrackJson = JsonSerializer.Serialize(decoded.Track, RouteCatalog.Json);
        row.TrackPointCount = decoded.Track.Count;
        row.OriginalStatus = decoded.Track.Count > 0 ? "imported" : "no-track";
        if (decoded.Exercises.Count > 0)
        {
            row.ExercisesJson = JsonSerializer.Serialize(decoded.Exercises, RouteCatalog.Json);
            row.Repetitions = decoded.Exercises.Sum(x => x.Repetitions ?? 0); row.Sets = decoded.Exercises.Count;
            row.VolumeKg = decoded.Exercises.Any(x => x.VolumeKg.HasValue) ? decoded.Exercises.Sum(x => x.VolumeKg ?? 0) : null;
        }
    }
}

public sealed record ChallengeDefinition(string Id, string Name, string Description, string Metric, string Unit, double Target, string ComparisonNote);
