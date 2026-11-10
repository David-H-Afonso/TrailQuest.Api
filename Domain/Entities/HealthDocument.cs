namespace TrailQuest.Api.Domain.Entities;

// Original successful DATA responses, never token responses or HTTP headers.
public sealed class HealthDocument
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = "";
    public string Dataset { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Source { get; set; } = "garmin";
    public string PayloadJson { get; set; } = "{}";
    public string ContentHash { get; set; } = "";
    public DateTimeOffset RetrievedAtUtc { get; set; }
}

public sealed class GarminSyncSettings
{
    public string UserId { get; set; } = "";
    public string DatasetIdsJson { get; set; } = "[\"steps\",\"activities\",\"daily-summary\",\"sleep\",\"heart-rate\",\"body-battery\",\"hrv\"]";
    public int IntervalHours { get; set; }
    public int LookbackDays { get; set; } = 7;
    public bool ImportOriginals { get; set; } = true;
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? LastCompletedAtUtc { get; set; }
    public string LastResultJson { get; set; } = "[]";
}
