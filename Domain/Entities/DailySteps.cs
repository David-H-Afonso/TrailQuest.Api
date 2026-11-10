namespace TrailQuest.Api.Domain.Entities;

public sealed class DailySteps
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = "personal";
    public DateOnly Date { get; set; }
    public int Steps { get; set; }
    public double DistanceMeters { get; set; }
    public double StepLengthCm { get; set; } = 72;
    public bool IsDistanceEstimated { get; set; } = true;
    public string Source { get; set; } = "manual";
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
