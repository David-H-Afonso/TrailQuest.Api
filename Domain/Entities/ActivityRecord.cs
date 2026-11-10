namespace TrailQuest.Api.Domain.Entities;

public sealed class ActivityRecord
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Source { get; set; } = "garmin";
    public string Name { get; set; } = "";
    public string Sport { get; set; } = "other";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateOnly Date { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public double? DurationSeconds { get; set; }
    public double? DistanceMeters { get; set; }
    public double? CaloriesKcal { get; set; }
    public double? AverageHeartRate { get; set; }
    public int? Repetitions { get; set; }
    public int? Sets { get; set; }
    public double? VolumeKg { get; set; }
    public string ExercisesJson { get; set; } = "[]";
    public string TrackJson { get; set; } = "[]";
    public int TrackPointCount { get; set; }
    public byte[]? OriginalFile { get; set; }
    public string? OriginalFormat { get; set; }
    public string? OriginalHash { get; set; }
    public string OriginalStatus { get; set; } = "not-requested";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
