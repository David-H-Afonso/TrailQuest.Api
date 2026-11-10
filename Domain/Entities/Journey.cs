namespace TrailQuest.Api.Domain.Entities;

public sealed class Journey
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = "personal";
    public string RouteId { get; set; } = "";
    public string ContentVersion { get; set; } = "1.0.0";
    public string DefinitionJson { get; set; } = "";
    public DateOnly StartedOn { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? LeftAtUtc { get; set; }
    public long? SavedSteps { get; set; }
    public double? SavedDistanceKm { get; set; }
    public DateOnly? CountingFrom { get; set; }
    public int InitialDaySteps { get; set; }
    public double InitialDayDistanceKm { get; set; }
}
