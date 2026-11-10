using System.Text.Json;

namespace TrailQuest.Api.Contracts;

public sealed record TrackPointDto(double Latitude, double Longitude, DateTimeOffset? AtUtc = null, double? ElevationMeters = null);
public sealed record ExerciseDto(string Name, int? Sets, int? Repetitions, double? MaxWeightKg, double? VolumeKg);
public sealed record ActivityDto(long Id, string Name, string Sport, string Source, DateTimeOffset StartedAtUtc,
    double? DurationSeconds, double? DistanceMeters, double? CaloriesKcal, double? AverageHeartRate,
    int? Repetitions, int? Sets, double? VolumeKg, IReadOnlyList<ExerciseDto> Exercises,
    string OriginalStatus, int TrackPoints);
public sealed record ActivityDetailDto(ActivityDto Activity, IReadOnlyList<TrackPointDto> Track);
public sealed record MetricDto(string Id, string Label, double? Value, string Unit, DateOnly Date, string Source);
public sealed record MetricSampleDto(DateTimeOffset AtUtc, double Value);
public sealed record MetricSeriesDto(string Id, string Label, string Unit, IReadOnlyList<MetricSampleDto> Samples);
public sealed record HealthDayDto(DateOnly Date, IReadOnlyList<MetricDto> Metrics, IReadOnlyList<MetricSeriesDto> Series);
public sealed record DatasetCoverageDto(string Id, int Documents, DateTimeOffset? UpdatedAtUtc);
public sealed record HealthOverviewDto(string ProfileId, DateOnly StartDate, DateOnly EndDate,
    IReadOnlyList<ActivityDto> Activities, IReadOnlyList<HealthDayDto> Days,
    IReadOnlyList<DatasetCoverageDto> Coverage, DateTimeOffset? LastSyncAtUtc);
public sealed record DatasetDto(string Id, string Name, string Description, bool Sensitive, string Granularity);
public sealed record DatasetSyncResultDto(string Dataset, string Status, int Records, string? Message = null);
public sealed record FullSyncResultDto(DateTimeOffset CompletedAtUtc, IReadOnlyList<DatasetSyncResultDto> Results);
public sealed record SyncPreferencesDto(IReadOnlyList<string> Datasets, int IntervalHours, int LookbackDays,
    bool ImportOriginals, DateTimeOffset? LastAttemptAtUtc, DateTimeOffset? LastCompletedAtUtc,
    IReadOnlyList<DatasetSyncResultDto> LastResults);
public sealed record SaveSyncPreferencesRequest(IReadOnlyList<string> Datasets, int IntervalHours, int LookbackDays, bool ImportOriginals);
public sealed record FullSyncRequest(DateOnly? StartDate, DateOnly? EndDate);
public sealed record RawDocumentDto(long Id, string Dataset, DateOnly Date, string Source, DateTimeOffset RetrievedAtUtc, JsonElement Data);
public sealed record ChallengeDto(string Id, string Name, string Description, string Metric, string Unit,
    double Target, double Value, double ProgressPercent, bool Completed, string ComparisonNote);
