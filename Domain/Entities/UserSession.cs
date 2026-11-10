namespace TrailQuest.Api.Domain.Entities;

public sealed class UserSession
{
    public string IdHash { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
