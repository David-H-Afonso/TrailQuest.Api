using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Application;

public sealed class SyncCoordinator
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> users = new();
    public SemaphoreSlim For(string id) => users.GetOrAdd(id, _ => new(1, 1));
}

public sealed class GarminSyncService(TrailQuestDbContext db, CurrentUser current, HealthService health,
    AdventureService adventures, GarminConnectionService connection, TimeProvider clock, LocalMutationGate gate, SyncCoordinator coordinator)
{
    private static IReadOnlyList<T> Read<T>(string json) => JsonSerializer.Deserialize<List<T>>(json, RouteCatalog.Json) ?? [];
    private static SyncPreferencesDto ToDto(GarminSyncSettings row) => new(Read<string>(row.DatasetIdsJson), row.IntervalHours,
        row.LookbackDays, row.ImportOriginals, row.LastAttemptAtUtc, row.LastCompletedAtUtc, Read<DatasetSyncResultDto>(row.LastResultJson));
    public async Task<SyncPreferencesDto> PreferencesAsync(CancellationToken ct)
    {
        var id = current.Id;
        return ToDto(await db.SyncSettings.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, ct) ?? new GarminSyncSettings());
    }
    public async Task SavePreferencesAsync(SaveSyncPreferencesRequest request, CancellationToken ct)
    {
        if (request.Datasets is null || request.Datasets.Count > GarminDatasets.All.Count || request.Datasets.Distinct().Count() != request.Datasets.Count ||
            request.Datasets.Any(x => !GarminDatasets.All.Any(d => d.Id == x)) || request.IntervalHours is not (0 or 12 or 24) || request.LookbackDays is < 1 or > 28)
            throw new AppException("invalid_sync_preferences", 400, "Select valid datasets, a 1–28 day recent window and manual, 12h or 24h synchronization.");
        var id = current.Id;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await db.SyncSettings.SingleOrDefaultAsync(x => x.UserId == id, ct);
            if (row is null) { row = new() { UserId = id }; db.SyncSettings.Add(row); }
            row.DatasetIdsJson = JsonSerializer.Serialize(request.Datasets); row.IntervalHours = request.IntervalHours;
            row.LookbackDays = request.LookbackDays; row.ImportOriginals = request.ImportOriginals;
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }

    public async Task<FullSyncResultDto> SyncAsync(FullSyncRequest request, CancellationToken ct)
    {
        var id = current.Id;
        var userGate = coordinator.For(id);
        if (!await userGate.WaitAsync(0, ct)) throw new AppException("sync_running", 409, "This account already has an import in progress.");
        try
        {
            var profile = await health.ProfileAsync("personal", ct);
            var settings = await PreferencesAsync(ct);
            var end = request.EndDate ?? health.Today(profile); var start = request.StartDate ?? end.AddDays(1 - settings.LookbackDays);
            HealthService.ValidateRange(start, end, 28);
            if (end > health.Today(profile)) throw new AppException("invalid_range", 400, "Cannot import future days.");
            var version = await connection.GetVersionAsync(ct) ?? throw new AppException("garmin_not_configured", 409, "Configure your Garmin connection first.");
            var results = new List<DatasetSyncResultDto>();
            await SaveRunAsync(results, false, ct);
            foreach (var dataset in settings.Datasets)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var records = 0;
                    if (dataset == "activities") records = await ActivitiesAsync(profile, start, end, settings.ImportOriginals, version, ct);
                    else if (dataset == "steps")
                    {
                        var payload = await connection.ReadAsync($"/usersummary-service/stats/steps/daily/{start:yyyy-MM-dd}/{end:yyyy-MM-dd}", ct);
                        var days = GarminGateway.ParseDays(payload, start, end);
                        await adventures.ApplyGarminDaysAsync("personal", days, version, ct);
                        await StoreAsync(profile, dataset, start.ToString("yyyy-MM-dd"), start, payload, version, ct);
                        records = days.Count;
                    }
                    else foreach (var query in GarminDatasets.Requests(dataset, start, end))
                    {
                        var payload = await connection.ReadAsync(query.Path, ct);
                        await StoreAsync(profile, dataset, query.Key, query.Date, payload, version, ct);
                        records += HealthNormalizer.Items(payload).Count;
                    }
                    results.Add(new(dataset, records == 0 ? "empty" : "success", records));
                }
                catch (AppException error) when (error.Code != "garmin_connection_changed")
                {
                    results.Add(new(dataset, "error", 0, error.Message));
                    if (error.Code is "garmin_auth" or "garmin_rate_limit" or "garmin_key_unavailable")
                    {
                        results.AddRange(settings.Datasets.Skip(results.Count).Select(x => new DatasetSyncResultDto(x, "skipped", 0, "Import stopped to protect the connection or request limits.")));
                        break;
                    }
                }
                await SaveRunAsync(results, false, ct);
            }
            await SaveRunAsync(results, true, ct);
            return new(clock.GetUtcNow(), results);
        }
        finally { userGate.Release(); }
    }

    private async Task StoreAsync(ProfileSettings profile, string dataset, string key, DateOnly date, JsonElement payload, string version, CancellationToken ct)
    {
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await CheckVersionAsync(version, ct);
            await health.ImportDocumentAsync(profile, dataset, key, date, payload, ct);
            // Preserve envelope fields as well as normalized per-record documents.
            if (payload.ValueKind == JsonValueKind.Object && new[] { "data", "dailySleepDTOs", "hrvSummaries", "dateWeightList", "dailyWeightSummaries" }.Any(x => HealthNormalizer.At(payload, x).ValueKind == JsonValueKind.Array))
                await health.ImportDocumentAsync(profile, dataset + "-envelope", key, date,
                    JsonSerializer.SerializeToElement(new { response = payload }), ct);
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }
    private async Task CheckVersionAsync(string version, CancellationToken ct)
    {
        if (version != await connection.GetVersionAsync(ct)) throw new AppException("garmin_connection_changed", 409, "The connection changed during import. Start again.");
    }
    private async Task SaveRunAsync(IReadOnlyList<DatasetSyncResultDto> results, bool completed, CancellationToken ct)
    {
        var id = current.Id;
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            var row = await db.SyncSettings.SingleOrDefaultAsync(x => x.UserId == id, ct);
            if (row is null) { row = new() { UserId = id }; db.SyncSettings.Add(row); }
            if (results.Count == 0) row.LastAttemptAtUtc = clock.GetUtcNow();
            row.LastResultJson = JsonSerializer.Serialize(results, RouteCatalog.Json);
            if (completed) row.LastCompletedAtUtc = clock.GetUtcNow();
            if (completed && results.Any(x => x.Status == "success"))
            {
                var profile = await db.Profiles.SingleAsync(x => x.UserId == id && x.Kind == "personal", ct);
                profile.LastGarminSyncAtUtc = clock.GetUtcNow();
            }
            await db.SaveChangesAsync(ct);
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task<int> ActivitiesAsync(ProfileSettings profile, DateOnly start, DateOnly end, bool originals, string version, CancellationToken ct)
    {
        var count = 0; var originalsThisRun = 0;
        for (var page = 0; page < 10; page++)
        {
            var list = await connection.ReadAsync($"/activitylist-service/activities/search/activities?start={page * 50}&limit=50&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}&excludeChildren=true", ct);
            var items = HealthNormalizer.Items(list);
            foreach (var item in items)
            {
                if (!long.TryParse(HealthNormalizer.Text(item, "activityId"), out var activityId) || activityId <= 0)
                    throw new AppException("garmin_response", 502, "An activity has no valid identifier.");
                var sourceId = activityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var existing = await db.Activities.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.Source == "garmin" && x.SourceId == sourceId, ct);
                // List summaries are refreshed; detail/workout payloads are fetched once per new activity.
                var detail = item;
                JsonElement? workout = null;
                if (existing is null)
                {
                    try { detail = await connection.ReadAsync($"/activity-service/activity/{activityId}", ct); }
                    catch (AppException error) when (error.Code is not ("garmin_auth" or "garmin_rate_limit")) { /* The list summary remains useful. */ }
                    try { workout = await connection.ReadAsync($"/activity-service/activity/{activityId}/workouts", ct); }
                    catch (AppException error) when (error.Code is not ("garmin_auth" or "garmin_rate_limit")) { /* A missing workout is not a missing activity. */ }
                }
                byte[]? bytes = null; DecodedActivity? decoded = null; string? fileError = null;
                if (originals && existing?.OriginalFile is null && originalsThisRun < 10)
                {
                    originalsThisRun++;
                    try { bytes = await connection.DownloadAsync(activityId, ct); decoded = ActivityFileReader.Read(bytes); }
                    catch (AppException error) when (error.Code is not ("garmin_auth" or "garmin_rate_limit")) { fileError = "unavailable"; }
                }
                await gate.Semaphore.WaitAsync(ct);
                try
                {
                    await CheckVersionAsync(version, ct);
                    if (decoded is not null && existing is null)
                        existing = await db.Activities.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.OriginalHash == decoded.Hash, ct);
                    var row = existing ?? new ActivityRecord { ProfileId = profile.Id };
                    row.Source = "garmin"; row.SourceId = sourceId;
                    ApplySummary(row, item, detail, profile);
                    if (decoded is not null && bytes is not null) HealthService.ApplyFile(row, bytes, decoded);
                    else if (fileError is not null) row.OriginalStatus = fileError;
                    else if (originals && row.OriginalFile is null) row.OriginalStatus = "pending";
                    if (existing is null) db.Activities.Add(row);
                    await health.ImportDocumentAsync(profile, "activity-summary", sourceId, row.Date, item, ct);
                    if (existing is null || detail.GetRawText() != item.GetRawText()) await health.ImportDocumentAsync(profile, "activity-detail", sourceId, row.Date, detail, ct);
                    if (workout is not null) await health.ImportDocumentAsync(profile, "activity-workout", sourceId, row.Date, workout.Value, ct);
                    await db.SaveChangesAsync(ct);
                    db.Entry(row).State = EntityState.Detached;
                }
                finally { gate.Semaphore.Release(); }
                count++;
            }
            if (items.Count < 50) return count;
        }
        throw new AppException("sync_page_limit", 409, "Imported the first 500 activities. Select a smaller date range to finish the remaining history.");
    }

    private void ApplySummary(ActivityRecord row, JsonElement list, JsonElement detail, ProfileSettings profile)
    {
        var summary = HealthNormalizer.At(detail, "summaryDTO");
        if (summary.ValueKind != JsonValueKind.Object) summary = list;
        double? Value(string name) => HealthNormalizer.Number(summary, name) ?? HealthNormalizer.Number(list, name);
        var timestamp = HealthNormalizer.At(list, "beginTimestamp");
        var started = HealthNormalizer.Timestamp(timestamp) ?? HealthNormalizer.Timestamp(HealthNormalizer.At(summary, "startTimeGMT"))
            ?? HealthNormalizer.Timestamp(HealthNormalizer.At(list, "startTimeGMT"))
            ?? throw new AppException("garmin_response", 502, "An activity has no usable start time.");
        int? Count(double? value) => value is >= 0 and <= 1_000_000 ? (int)value : null;
        var repetitions = Count(Value("totalExerciseReps") ?? Value("totalReps"));
        var sets = Count(Value("activeSets"));
        List<ExerciseDto>? exercises = null;
        var exerciseItems = HealthNormalizer.At(list, "summarizedExerciseSets");
        if (exerciseItems.ValueKind == JsonValueKind.Array)
            exercises = exerciseItems.EnumerateArray().Select(x => new ExerciseDto(
                HealthNormalizer.Text(x, "subCategory", "category") ?? "Ejercicio", Count(HealthNormalizer.Number(x, "sets")),
                Count(HealthNormalizer.Number(x, "reps")), HealthNormalizer.Number(x, "maxWeight") / 1000, HealthNormalizer.Number(x, "volume") / 1000)).ToList();
        row.Name = (HealthNormalizer.Text(list, "activityName") ?? "Actividad Garmin")[..Math.Min((HealthNormalizer.Text(list, "activityName") ?? "Actividad Garmin").Length, 200)];
        row.Sport = HealthNormalizer.Text(list, "activityType.typeKey", "activityTypeDTO.typeKey") ?? "other";
        row.StartedAtUtc = started;
        row.TimeZoneId = profile.TimeZoneId;
        row.Date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(started, TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId)).DateTime);
        row.DurationSeconds = Value("duration") is >= 0 and <= 604800 ? Value("duration") : null;
        row.DistanceMeters = Value("distance"); row.CaloriesKcal = Value("calories");
        row.AverageHeartRate = Value("averageHR") ?? Value("averageHeartRate");
        if (row.OriginalFormat != "fit" || row.ExercisesJson == "[]")
        {
            row.Repetitions = repetitions; row.Sets = sets;
            if (exercises is not null)
            {
                row.ExercisesJson = JsonSerializer.Serialize(exercises, RouteCatalog.Json);
                row.VolumeKg = exercises.Any(x => x.VolumeKg.HasValue) ? exercises.Sum(x => x.VolumeKg ?? 0) : null;
                row.Repetitions ??= exercises.Any(x => x.Repetitions.HasValue) ? exercises.Sum(x => x.Repetitions ?? 0) : null;
                row.Sets ??= exercises.Any(x => x.Sets.HasValue) ? exercises.Sum(x => x.Sets ?? 0) : null;
            }
        }
        row.UpdatedAtUtc = clock.GetUtcNow();
    }
}
