namespace TrailQuest.Api.Domain.Entities;

public sealed class ProfileSettings
{
    public string Id { get; set; } = "personal";
    public string? UserId { get; set; }
    public string Kind { get; set; } = "personal";
    public int DailyStepGoal { get; set; } = 8000;
    public double StepLengthCm { get; set; } = 72;
    public string TimeZoneId { get; set; } = TimeZoneIds.ForClient(TimeZoneInfo.Local.Id);
    public string SelectedRouteId { get; set; } = "";
    public DateTimeOffset? LastGarminSyncAtUtc { get; set; }
}
