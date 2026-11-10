namespace TrailQuest.Api.Domain.Entities;

public sealed class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "";
    public string NormalizedUsername { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsOwner { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
