namespace TrailQuest.Api.Domain.Entities;

public sealed class AchievementUnlock
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = "personal";
    public string AchievementId { get; set; } = "";
    public DateTimeOffset UnlockedAtUtc { get; set; }
}
