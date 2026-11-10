namespace TrailQuest.Api.Domain.Entities;

public sealed class CheckpointVisit
{
    public long Id { get; set; }
    public long JourneyId { get; set; }
    public string CheckpointId { get; set; } = "";
    public DateTimeOffset UnlockedAtUtc { get; set; }
}
