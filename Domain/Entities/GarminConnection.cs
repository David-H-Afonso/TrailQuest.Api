namespace TrailQuest.Api.Domain.Entities;

public sealed class GarminConnection
{
    public string UserId { get; set; } = "";
    public string InstallationId { get; set; } = "";
    public string ProtectedApiKey { get; set; } = "";
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset VerifiedAtUtc { get; set; }
}
