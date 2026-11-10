namespace TrailQuest.Api.Contracts;

public sealed record UserDto(string Id, string Username, string DisplayName, bool IsOwner);
public sealed record SessionDto(UserDto? User, bool SetupRequired, DateTimeOffset? ExpiresAtUtc = null);
public sealed record GarminConnectionDto(bool Configured, DateTimeOffset? VerifiedAtUtc);

// Request objects intentionally do not generate record ToString output containing secrets.
public sealed class LoginRequest
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public override string ToString() => "[Redacted login request]";
}

public sealed class CreateUserRequest
{
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Password { get; init; } = "";
    public override string ToString() => "[Redacted account request]";
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = "";
    public string NewPassword { get; init; } = "";
    public override string ToString() => "[Redacted password request]";
}

public sealed class SaveGarminKeyRequest
{
    public string ApiKey { get; init; } = "";
    public override string ToString() => "[Redacted Garmin credential request]";
}
