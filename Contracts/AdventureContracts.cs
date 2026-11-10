namespace TrailQuest.Api.Contracts;

public sealed record CheckpointDto(
    string Id, string Name, string LocalName, double DistanceKm,
    double[] Position, string Category, string Description, string Fact)
{
    public string[] Story { get; init; } = [];
    public string Stamp { get; init; } = "compass";
    public string? Image { get; init; }
    public IReadOnlyList<AdventureMediaDto> Gallery { get; init; } = [];
}

public sealed record AdventureMediaDto(string File, string Alt, string Caption, string Credit, string License)
{
    public string? SourceUrl { get; init; }
    public string? LicenseUrl { get; init; }
}
public sealed record PassportDefinition(string Title, string Subtitle, string RegionLabel, string Color, string Motif, string ClosingText);
public sealed record MapDefinition(string Kind, string? Artwork = null, string Label = "Recorrido virtual");
public sealed record ContentReference(string Title, string Url);

public sealed record AdventureRouteDto(
    string Id, string Name, string Subtitle, string Region, string Kind,
    string Difficulty, double DistanceKm, string Description, string Color,
    double[][] Coordinates, IReadOnlyList<CheckpointDto> Checkpoints, string Attribution)
{
    public int SchemaVersion { get; init; } = 1;
    public string ContentVersion { get; init; } = "1.0.0";
    public bool IsDefault { get; init; }
    public string Setting { get; init; } = "real";
    public string Introduction { get; init; } = "";
    public string? Cover { get; init; }
    public PassportDefinition Passport { get; init; } = new("", "", "", "#147d80", "compass", "Un viaje para recordar.");
    public MapDefinition Map { get; init; } = new("geographic");
    public IReadOnlyList<AdventureMediaDto> Gallery { get; init; } = [];
    public IReadOnlyList<ContentReference> References { get; init; } = [];
}

public sealed record SettingsDto(int DailyStepGoal, double StepLengthCm, string TimeZoneId);

public sealed record DailyStepsDto(
    DateOnly Date, int Steps, double DistanceKm, bool IsDistanceEstimated,
    string Source, DateTimeOffset? UpdatedAtUtc);

public sealed record TotalsDto(
    long Steps, double DistanceKm, int ActiveDays, long Xp,
    long Level, long LevelXp, int XpPerLevel, int GoalDays);

public sealed record CheckpointVisitDto(string CheckpointId, DateTimeOffset UnlockedAtUtc);

public sealed record JourneyDto(
    string RouteId, DateOnly StartedOn, long Steps, double DistanceKm,
    double ProgressPercent, double RemainingKm, string? NextCheckpointId,
    double NextCheckpointRemainingKm, IReadOnlyList<CheckpointVisitDto> Visits)
{
    public string ContentVersion { get; init; } = "1.0.0";
    public AdventureRouteDto? Definition { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset? LeftAtUtc { get; init; }
    public DateOnly? CountingFrom { get; init; }
}

public sealed record AchievementDto(
    string Id, string Name, string Description, string Icon,
    bool Unlocked, DateTimeOffset? UnlockedAtUtc);

public sealed record GarminStatusDto(bool Configured, DateTimeOffset? LastSyncAtUtc);

public sealed record DashboardDto(
    string ProfileId, DateOnly Today, SettingsDto Settings, DailyStepsDto TodayActivity,
    IReadOnlyList<DailyStepsDto> Week, IReadOnlyList<DailyStepsDto> History,
    TotalsDto Totals, JourneyDto? CurrentJourney, IReadOnlyList<JourneyDto> Journeys,
    IReadOnlyList<AchievementDto> Achievements, GarminStatusDto Garmin)
{
    public string JourneyPolicy { get; init; } = "single-active";
}

public sealed record SaveDailyStepsRequest([property: System.Text.Json.Serialization.JsonRequired] int Steps);
public sealed record SaveSettingsRequest(int DailyStepGoal, double StepLengthCm, string TimeZoneId);
public sealed record StartJourneyRequest(string RouteId, DateOnly? StartedOn, string? ReplacingRouteId = null);
public sealed record UpdateJourneyRequest(DateOnly StartedOn);
public sealed record SyncGarminRequest(DateOnly? StartDate, DateOnly? EndDate);
public sealed record SyncResultDto(int DaysReceived, int DaysChanged, DateTimeOffset SyncedAtUtc);
public sealed record ExportDailyStepsDto(
    DateOnly Date, int Steps, double DistanceKm, double StepLengthCm,
    bool IsDistanceEstimated, string Source, DateTimeOffset UpdatedAtUtc);
public sealed record ExportDto(
    int SchemaVersion, DateTimeOffset ExportedAtUtc, string ProfileId,
    SettingsDto Settings, string SelectedRouteId, IReadOnlyList<ExportDailyStepsDto> Days,
    IReadOnlyList<JourneyDto> Journeys, IReadOnlyList<AchievementDto> Achievements)
{
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public IReadOnlyList<ActivityDetailDto> Activities { get; init; } = [];
    public IReadOnlyList<RawDocumentDto> HealthDocuments { get; init; } = [];
}
