using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Application;

public sealed class AdventureService(
    TrailQuestDbContext db, RouteCatalog catalog, IGarminGateway garmin,
    TimeProvider clock, LocalMutationGate gate, CurrentUser current, DemoData demo, HealthService health)
{
    private static readonly (string Id, string Name, string Description, string Icon)[] AchievementDefinitions =
    [
        ("first-step", "El primer paso", "Registra tus primeros pasos. Todo viaje empieza así.", "footprints"),
        ("five-km", "Cogiendo camino", "Acumula tus primeros 5 kilómetros.", "signpost"),
        ("ten-km", "Ya estás lejos", "Recorre 10 kilómetros, a tu ritmo.", "compass"),
        ("first-discovery", "Un lugar nuevo", "Descubre tu primera parada después de la salida.", "map-pin"),
        ("daily-goal", "Un día redondo", "Alcanza tu objetivo diario al menos una vez.", "sun"),
        ("seven-days", "Pequeños hábitos", "Suma pasos en 7 días distintos. No tienen que ser seguidos.", "calendar"),
        ("first-route", "De principio a fin", "Completa una de tus aventuras.", "flag")
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await db.Users.AnyAsync(cancellationToken))
            {
                foreach (var kind in new[] { "personal", "demo" })
                {
                    if (await db.Profiles.AnyAsync(x => x.Id == kind, cancellationToken)) continue;
                    var profile = new ProfileSettings { Id = kind, Kind = kind };
                    db.Profiles.Add(profile);
                    SeedProfile(profile);
                }
            }
            await db.SaveChangesAsync(cancellationToken);
            foreach (var profile in await db.Profiles.ToListAsync(cancellationToken))
            {
                foreach (var journey in await db.Journeys.Where(x => x.ProfileId == profile.Id && x.DefinitionJson == "").ToListAsync(cancellationToken))
                {
                    var definition = catalog.Get(journey.RouteId);
                    journey.ContentVersion = definition.ContentVersion;
                    journey.DefinitionJson = JsonSerializer.Serialize(definition, RouteCatalog.Json);
                }
                var active = await db.Journeys.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.LeftAtUtc == null, cancellationToken);
                profile.SelectedRouteId = active?.RouteId ?? "";
                await RefreshRewardsAsync(profile, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { gate.Semaphore.Release(); }
    }

    // AccountService calls this inside its serialized account-creation transaction.
    internal async Task ProvisionProfilesAsync(string userId, bool claimLegacy, CancellationToken cancellationToken)
    {
        foreach (var kind in new[] { "personal", "demo" })
        {
            var profile = claimLegacy
                ? await db.Profiles.SingleOrDefaultAsync(x => x.Id == kind && x.UserId == null, cancellationToken)
                : null;
            if (profile is null)
            {
                profile = new ProfileSettings { Id = Guid.NewGuid().ToString("N"), Kind = kind, UserId = userId };
                db.Profiles.Add(profile);
                SeedProfile(profile);
            }
            else { profile.UserId = userId; profile.Kind = kind; }
            await db.SaveChangesAsync(cancellationToken);
            await RefreshRewardsAsync(profile, cancellationToken);
        }
    }

    public async Task<DashboardDto> GetDashboardAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(profileId, cancellationToken);
        var today = Today(profile);
        var days = await db.DailySteps.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Date <= today)
            .OrderBy(x => x.Date).ToListAsync(cancellationToken);
        var journeys = await GetJourneysAsync(profile.Id, days, today, cancellationToken);
        var byDate = days.ToDictionary(x => x.Date);
        var week = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = today.AddDays(offset - 6);
            return byDate.TryGetValue(date, out var day) ? ToDto(day) : EmptyDay(date);
        }).ToList();
        return new DashboardDto(
            profileId, today, ToDto(profile), byDate.TryGetValue(today, out var activity) ? ToDto(activity) : EmptyDay(today),
            week, days.OrderByDescending(x => x.Date).Take(90).Select(ToDto).ToList(),
            ProgressCalculator.Totals(days, profile.DailyStepGoal, today),
            journeys.SingleOrDefault(x => x.IsActive), journeys,
            await GetAchievementsAsync(profile.Id, cancellationToken),
            new GarminStatusDto(await garmin.IsConfiguredAsync(cancellationToken), profile.LastGarminSyncAtUtc));
    }

    public async Task SaveDayAsync(string profileId, DateOnly date, int steps, CancellationToken cancellationToken)
    {
        ValidateSteps(steps);
        await WriteAsync(profileId, async profile =>
        {
            ValidateDate(date, Today(profile));
            var day = await db.DailySteps.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.Date == date, cancellationToken);
            if (day is null)
            {
                day = new DailySteps { ProfileId = profile.Id, Date = date };
                db.DailySteps.Add(day);
            }
            day.Steps = steps;
            day.StepLengthCm = profile.StepLengthCm;
            day.DistanceMeters = steps * profile.StepLengthCm / 100;
            day.IsDistanceEstimated = true;
            day.Source = profile.Kind == "demo" ? "demo" : "manual";
            day.UpdatedAtUtc = clock.GetUtcNow();
        }, cancellationToken);
    }

    public Task AdvanceDemoAsync(CancellationToken cancellationToken) => WriteAsync("demo", async profile =>
    {
        var today = Today(profile);
        var day = await db.DailySteps.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.Date == today, cancellationToken);
        if (day is null)
        {
            day = new DailySteps { ProfileId = profile.Id, Date = today, StepLengthCm = profile.StepLengthCm };
            db.DailySteps.Add(day);
        }
        ValidateSteps(day.Steps + 1000);
        day.Steps += 1000;
        day.DistanceMeters = day.Steps * day.StepLengthCm / 100;
        day.Source = "demo";
        day.UpdatedAtUtc = clock.GetUtcNow();
    }, cancellationToken);

    public Task ResetDemoAsync(CancellationToken cancellationToken) => WriteAsync("demo", async profile =>
    {
        await db.DailySteps.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await db.Activities.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await db.HealthDocuments.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await db.AchievementUnlocks.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await db.Journeys.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        profile.DailyStepGoal = 8000;
        profile.StepLengthCm = 72;
        profile.TimeZoneId = Domain.TimeZoneIds.ForClient(TimeZoneInfo.Local.Id);
        profile.LastGarminSyncAtUtc = null;
        profile.SelectedRouteId = catalog.Default.Id;
        SeedProfile(profile);
    }, cancellationToken);

    public Task SaveSettingsAsync(string profileId, SaveSettingsRequest request, CancellationToken cancellationToken)
    {
        if (request.DailyStepGoal is < 1000 or > 50000 || !double.IsFinite(request.StepLengthCm) ||
            request.StepLengthCm is < 30 or > 150 || string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 100)
            throw new AppException("invalid_settings", 400, "Use a daily goal from 1000 to 50000 and a step length from 30 to 150 cm.");
        try { TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new AppException("invalid_timezone", 400, "The requested time zone is not supported.");
        }
        return WriteAsync(profileId, profile =>
        {
            profile.DailyStepGoal = request.DailyStepGoal;
            profile.StepLengthCm = Math.Round(request.StepLengthCm, 1);
            profile.TimeZoneId = request.TimeZoneId;
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public Task StartJourneyAsync(string profileId, StartJourneyRequest request, CancellationToken cancellationToken)
    {
        var definition = catalog.Get(request.RouteId);
        return WriteAsync(profileId, async profile =>
        {
            var active = await db.Journeys.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.LeftAtUtc == null, cancellationToken);
            if (active?.RouteId != request.RouteId && active?.RouteId != request.ReplacingRouteId)
                throw new AppException("active_journey_changed", 409, "Confirm the current active passport before replacing it. The active passport may have changed.");
            var journey = await db.Journeys.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.RouteId == request.RouteId, cancellationToken);
            if (journey?.LeftAtUtc is not null && request.StartedOn is not null)
                throw new AppException("journey_resume_date", 400, "Resuming preserves the original start date and counts newly recorded steps from today.");
            var date = request.StartedOn ?? Today(profile);
            if (journey is null) ValidateDate(date, Today(profile));
            if (active is not null && active.RouteId != request.RouteId)
            {
                await FreezeJourneyAsync(profile, active, cancellationToken);
                // Release the filtered unique key before another journey becomes active,
                // while retaining a single transaction for the whole switch.
                await db.SaveChangesAsync(cancellationToken);
            }
            if (journey is null)
            {
                db.Journeys.Add(new Journey { ProfileId = profile.Id, RouteId = request.RouteId, ContentVersion = definition.ContentVersion,
                    DefinitionJson = JsonSerializer.Serialize(definition, RouteCatalog.Json), StartedOn = date, CreatedAtUtc = clock.GetUtcNow() });
            }
            else if (journey.LeftAtUtc is not null)
            {
                var today = Today(profile);
                var day = await db.DailySteps.AsNoTracking().SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.Date == today, cancellationToken);
                journey.CountingFrom = today;
                journey.InitialDaySteps = day?.Steps ?? 0;
                journey.InitialDayDistanceKm = (day?.DistanceMeters ?? 0) / 1000;
                journey.LeftAtUtc = null;
            }
            profile.SelectedRouteId = request.RouteId;
        }, cancellationToken);
    }

    public Task LeaveJourneyAsync(string profileId, string routeId, CancellationToken cancellationToken)
        => WriteAsync(profileId, async profile =>
        {
            var journey = await db.Journeys.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.RouteId == routeId, cancellationToken)
                ?? throw new AppException("journey_not_found", 404, "This passport does not belong to this profile.");
            if (journey.LeftAtUtc is not null) return;
            await FreezeJourneyAsync(profile, journey, cancellationToken);
            profile.SelectedRouteId = "";
        }, cancellationToken);

    private async Task FreezeJourneyAsync(ProfileSettings profile, Journey journey, CancellationToken cancellationToken)
    {
        var days = await db.DailySteps.AsNoTracking().Where(x => x.ProfileId == profile.Id && x.Date <= Today(profile)).ToListAsync(cancellationToken);
        var progress = ProgressCalculator.JourneyTotals(journey, days, Today(profile));
        journey.SavedSteps = progress.Steps;
        journey.SavedDistanceKm = progress.DistanceKm;
        journey.LeftAtUtc = clock.GetUtcNow();
    }

    public Task UpdateJourneyAsync(string profileId, string routeId, UpdateJourneyRequest request, CancellationToken cancellationToken)
        => WriteAsync(profileId, async profile =>
        {
            ValidateDate(request.StartedOn, Today(profile));
            var journey = await db.Journeys.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.RouteId == routeId, cancellationToken)
                ?? throw new AppException("journey_not_found", 404, "This journey has not been started.");
            if (journey.LeftAtUtc is not null) throw new AppException("journey_inactive", 409, "Only the active passport can change its counting date.");
            journey.StartedOn = request.StartedOn;
            journey.SavedSteps = null;
            journey.SavedDistanceKm = null;
            journey.CountingFrom = null;
            journey.InitialDaySteps = 0;
            journey.InitialDayDistanceKm = 0;
        }, cancellationToken);

    public Task UpdateContentAsync(string profileId, string routeId, CancellationToken cancellationToken)
        => WriteAsync(profileId, async profile =>
        {
            var journey = await db.Journeys.SingleOrDefaultAsync(x => x.ProfileId == profile.Id && x.RouteId == routeId, cancellationToken)
                ?? throw new AppException("journey_not_found", 404, "This journey has not been started.");
            var previous = RouteCatalog.ForJourney(journey);
            var latest = catalog.Get(routeId);
            var compatible = previous.DistanceKm == latest.DistanceKm && previous.Map.Kind == latest.Map.Kind &&
                previous.Coordinates.Length == latest.Coordinates.Length && previous.Coordinates.Zip(latest.Coordinates).All(x => x.First.SequenceEqual(x.Second)) &&
                previous.Checkpoints.Count == latest.Checkpoints.Count && previous.Checkpoints.Zip(latest.Checkpoints)
                    .All(x => x.First.Id == x.Second.Id && x.First.DistanceKm == x.Second.DistanceKm && x.First.Position.SequenceEqual(x.Second.Position));
            if (!compatible) throw new AppException("content_update_incompatible", 409, "This edition changes route goals or geometry. The existing journey was preserved.");
            journey.ContentVersion = latest.ContentVersion;
            journey.DefinitionJson = JsonSerializer.Serialize(latest, RouteCatalog.Json);
        }, cancellationToken);

    public async Task<SyncResultDto> SyncGarminAsync(string profileId, SyncGarminRequest request, CancellationToken cancellationToken)
    {
        if (profileId != "personal")
            throw new AppException("personal_profile_required", 400, "Garmin synchronization is only available in the personal profile.");
        var profile = await GetProfileAsync(profileId, cancellationToken);
        var today = Today(profile);
        var end = request.EndDate ?? today;
        var start = request.StartDate ?? end.AddDays(-6);
        ValidateDate(start, today);
        ValidateDate(end, today);
        if (end < start || end.DayNumber - start.DayNumber > 27)
            throw new AppException("invalid_sync_range", 400, "Garmin daily steps requests support an inclusive range of up to 28 days.");

        // Fetch and validate the entire response before opening a database transaction.
        var version = await garmin.GetVersionAsync(cancellationToken);
        var days = await garmin.GetDaysAsync(start, end, cancellationToken);
        if (days.Any(x => x.Date < start || x.Date > end)) throw new AppException("garmin_response", 502, "Garmin returned a day outside the requested period. No data was changed.");
        return await ApplyGarminDaysAsync(profileId, days, version, cancellationToken);
    }

    internal async Task<SyncResultDto> ApplyGarminDaysAsync(string profileId, IReadOnlyList<GarminDay> days, string? version, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(profileId, cancellationToken);
        var today = Today(profile);
        var changed = 0;
        var syncedAt = clock.GetUtcNow();
        await WriteAsync(profileId, async currentProfile =>
        {
            if (version != await garmin.GetVersionAsync(cancellationToken))
                throw new AppException("garmin_connection_changed", 409, "The Garmin connection changed during synchronization. Try again.");
            foreach (var incoming in days)
            {
                ValidateSteps(incoming.Steps);
                ValidateDate(incoming.Date, today);
                if (incoming.DistanceMeters is < 0 or > 500000 ||
                    (incoming.DistanceMeters.HasValue && !double.IsFinite(incoming.DistanceMeters.Value)))
                    throw new AppException("garmin_response", 502, "Garmin returned an invalid date or distance. No data was changed.");
                var day = await db.DailySteps.SingleOrDefaultAsync(x => x.ProfileId == currentProfile.Id && x.Date == incoming.Date, cancellationToken);
                var stride = day?.StepLengthCm ?? currentProfile.StepLengthCm;
                var distance = incoming.DistanceMeters ?? incoming.Steps * stride / 100;
                var estimated = incoming.DistanceMeters is null;
                if (day is not null && day.Source == "garmin" && day.Steps == incoming.Steps &&
                    Math.Abs(day.DistanceMeters - distance) < 0.00001 && day.IsDistanceEstimated == estimated) continue;
                if (day is null)
                {
                    day = new DailySteps { ProfileId = currentProfile.Id, Date = incoming.Date, StepLengthCm = stride };
                    db.DailySteps.Add(day);
                }
                day.Steps = incoming.Steps;
                day.DistanceMeters = distance;
                day.IsDistanceEstimated = estimated;
                day.Source = "garmin";
                day.UpdatedAtUtc = syncedAt;
                changed++;
            }
            currentProfile.LastGarminSyncAtUtc = syncedAt;
        }, cancellationToken);
        return new SyncResultDto(days.Count, changed, syncedAt);
    }

    public Task<ExportDto> ExportAsync(string profileId, CancellationToken cancellationToken) => ExportAsync(profileId, null, null, cancellationToken);

    public async Task<ExportDto> ExportAsync(string profileId, DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(profileId, cancellationToken);
        if (startDate.HasValue != endDate.HasValue) throw new AppException("invalid_range", 400, "Supply both startDate and endDate.");
        if (startDate is { } start && endDate is { } end) HealthService.ValidateRange(start, end);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var activityQuery = db.Activities.AsNoTracking().Where(x => x.ProfileId == profile.Id && (startDate == null || x.Date >= startDate) && (endDate == null || x.Date <= endDate));
        var documentQuery = db.HealthDocuments.AsNoTracking().Where(x => x.ProfileId == profile.Id && (startDate == null || x.Date >= startDate) && (endDate == null || x.Date <= endDate));
        await ArchiveReadBudget.EnsureAsync(documentQuery, activityQuery, true, cancellationToken);
        var days = await db.DailySteps.AsNoTracking().Where(x => x.ProfileId == profile.Id).OrderBy(x => x.Date).ToListAsync(cancellationToken);
        var activities = await health.ActivityQuery(profile.Id, includeTrack: true).Where(x => (startDate == null || x.Date >= startDate) && (endDate == null || x.Date <= endDate)).ToListAsync(cancellationToken);
        var documents = await documentQuery.ToListAsync(cancellationToken);
        return new ExportDto(2, clock.GetUtcNow(), profileId, ToDto(profile), profile.SelectedRouteId,
            days.Where(x => (startDate == null || x.Date >= startDate) && (endDate == null || x.Date <= endDate)).Select(x => new ExportDailyStepsDto(x.Date, x.Steps, x.DistanceMeters / 1000,
                x.StepLengthCm, x.IsDistanceEstimated, x.Source, x.UpdatedAtUtc)).ToList(),
            await GetJourneysAsync(profile.Id, days, Today(profile), cancellationToken),
            await GetAchievementsAsync(profile.Id, cancellationToken))
        {
            StartDate = startDate, EndDate = endDate,
            Activities = activities.Select(x => new ActivityDetailDto(HealthService.ToDto(x), JsonSerializer.Deserialize<List<TrackPointDto>>(x.TrackJson, RouteCatalog.Json) ?? [])).ToList(),
            HealthDocuments = documents.Select(x => new RawDocumentDto(x.Id, x.Dataset, x.Date, x.Source, x.RetrievedAtUtc, JsonSerializer.Deserialize<JsonElement>(x.PayloadJson))).ToList()
        };
    }

    private async Task WriteAsync(string profileId, Func<ProfileSettings, Task> action, CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        await gate.Semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var profile = await GetProfileAsync(profileId, cancellationToken);
            await action(profile);
            await db.SaveChangesAsync(cancellationToken);
            await RefreshRewardsAsync(profile, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { gate.Semaphore.Release(); }
    }

    private async Task RefreshRewardsAsync(ProfileSettings profile, CancellationToken cancellationToken)
    {
        var profileId = profile.Id;
        var today = Today(profile);
        var days = await db.DailySteps.Where(x => x.ProfileId == profileId && x.Date <= today).ToListAsync(cancellationToken);
        var journeys = await db.Journeys.Where(x => x.ProfileId == profileId).ToListAsync(cancellationToken);
        var ids = journeys.Select(x => x.Id).ToList();
        var visits = await db.CheckpointVisits.Where(x => ids.Contains(x.JourneyId)).ToListAsync(cancellationToken);
        var discovered = false;
        var completed = false;
        foreach (var journey in journeys)
        {
            var route = RouteCatalog.ForJourney(journey);
            var progress = ProgressCalculator.Journey(journey, route, days, visits, today);
            completed |= progress.DistanceKm >= route.DistanceKm;
            discovered |= visits.Any(x => x.JourneyId == journey.Id && route.Checkpoints.Any(stop => stop.Id == x.CheckpointId && stop.DistanceKm > 0));
            if (journey.LeftAtUtc is not null && journey.SavedSteps.HasValue && journey.SavedDistanceKm.HasValue) continue;
            foreach (var stop in route.Checkpoints.Where(x => x.DistanceKm <= progress.DistanceKm + 0.000001))
            {
                discovered |= stop.DistanceKm > 0;
                if (visits.Any(x => x.JourneyId == journey.Id && x.CheckpointId == stop.Id)) continue;
                var visit = new CheckpointVisit { JourneyId = journey.Id, CheckpointId = stop.Id, UnlockedAtUtc = clock.GetUtcNow() };
                db.CheckpointVisits.Add(visit);
                visits.Add(visit);
            }
            if (journey.LeftAtUtc is not null)
            {
                // Legacy multi-passport repair: freeze once using the profile's actual
                // calendar boundary, without deleting recorded days or earned visits.
                var totalsAtExit = ProgressCalculator.JourneyTotals(journey, days, today);
                journey.SavedSteps = totalsAtExit.Steps;
                journey.SavedDistanceKm = totalsAtExit.DistanceKm;
            }
        }
        var totals = ProgressCalculator.Totals(days, profile.DailyStepGoal, today);
        var eligible = new Dictionary<string, bool>
        {
            ["first-step"] = totals.Steps > 0,
            ["five-km"] = totals.DistanceKm >= 5,
            ["ten-km"] = totals.DistanceKm >= 10,
            ["first-discovery"] = discovered,
            ["daily-goal"] = totals.GoalDays > 0,
            ["seven-days"] = totals.ActiveDays >= 7,
            ["first-route"] = completed
        };
        var unlocked = await db.AchievementUnlocks.Where(x => x.ProfileId == profileId).Select(x => x.AchievementId).ToListAsync(cancellationToken);
        foreach (var (id, shouldUnlock) in eligible)
            if (shouldUnlock && !unlocked.Contains(id))
                db.AchievementUnlocks.Add(new AchievementUnlock { ProfileId = profileId, AchievementId = id, UnlockedAtUtc = clock.GetUtcNow() });
    }

    private async Task<List<JourneyDto>> GetJourneysAsync(
        string profileId, IReadOnlyList<DailySteps> days, DateOnly today, CancellationToken cancellationToken)
    {
        var journeys = await db.Journeys.AsNoTracking().Where(x => x.ProfileId == profileId).ToListAsync(cancellationToken);
        var ids = journeys.Select(x => x.Id).ToList();
        var visits = await db.CheckpointVisits.AsNoTracking().Where(x => ids.Contains(x.JourneyId)).ToListAsync(cancellationToken);
        return journeys.Select(x =>
        {
            var definition = RouteCatalog.ForJourney(x);
            var progress = ProgressCalculator.Journey(x, definition, days, visits, today);
            return progress with { ContentVersion = x.ContentVersion,
                Definition = RouteCatalog.Visible(definition, progress.Visits.Select(v => v.CheckpointId).ToHashSet()) };
        }).OrderByDescending(x => x.IsActive).ThenBy(x => x.StartedOn).ThenBy(x => x.RouteId, StringComparer.Ordinal).ToList();
    }

    private async Task<List<AchievementDto>> GetAchievementsAsync(string profileId, CancellationToken cancellationToken)
    {
        var unlocks = await db.AchievementUnlocks.AsNoTracking().Where(x => x.ProfileId == profileId).ToListAsync(cancellationToken);
        return AchievementDefinitions.Select(x =>
        {
            var unlock = unlocks.SingleOrDefault(y => y.AchievementId == x.Id);
            return new AchievementDto(x.Id, x.Name, x.Description, x.Icon, unlock is not null, unlock?.UnlockedAtUtc);
        }).ToList();
    }

    private void SeedProfile(ProfileSettings profile)
    {
        var today = Today(profile);
        var definition = catalog.Default;
        profile.SelectedRouteId = definition.Id;
        db.Journeys.Add(new Journey
        {
            ProfileId = profile.Id, RouteId = definition.Id, ContentVersion = definition.ContentVersion,
            DefinitionJson = JsonSerializer.Serialize(definition, RouteCatalog.Json), StartedOn = profile.Kind == "demo" ? today.AddDays(-6) : today,
            CreatedAtUtc = clock.GetUtcNow()
        });
        if (profile.Kind != "demo") return;
        demo.Seed(db, profile, today, clock.GetUtcNow());
    }

    private async Task<ProfileSettings> GetProfileAsync(string profileId, CancellationToken cancellationToken)
    {
        ValidateProfile(profileId);
        var userId = current.Id;
        return await db.Profiles.SingleOrDefaultAsync(x => x.Kind == profileId && x.UserId == userId, cancellationToken)
            ?? throw new AppException("profile_not_found", 404, "The local profile is not initialized.");
    }

    private DateOnly Today(ProfileSettings profile) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(profile.TimeZoneId)).DateTime);

    private static void ValidateProfile(string profileId)
    {
        if (profileId is not ("demo" or "personal"))
            throw new AppException("profile_not_found", 404, "Only the demo and personal profiles are available.");
    }

    private static void ValidateDate(DateOnly date, DateOnly today)
    {
        if (date < new DateOnly(2000, 1, 1) || date > today)
            throw new AppException("invalid_date", 400, "Use a date from 2000-01-01 through today in your profile time zone.");
    }

    private static void ValidateSteps(int steps)
    {
        if (steps is < 0 or > 150000)
            throw new AppException("invalid_steps", 400, "The daily total must be between 0 and 150000 steps.");
    }

    private static SettingsDto ToDto(ProfileSettings profile) => new(profile.DailyStepGoal, profile.StepLengthCm, Domain.TimeZoneIds.ForClient(profile.TimeZoneId));
    private static DailyStepsDto ToDto(DailySteps day) => new(day.Date, day.Steps, day.DistanceMeters / 1000,
        day.IsDistanceEstimated, day.Source, day.UpdatedAtUtc);
    private static DailyStepsDto EmptyDay(DateOnly date) => new(date, 0, 0, true, "none", null);
}
