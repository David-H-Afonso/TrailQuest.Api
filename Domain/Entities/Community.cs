namespace TrailQuest.Api.Domain.Entities;

public sealed class Friendship
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RequesterId { get; set; } = "";
    public string RecipientId { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class UserBlock
{
    public string UserId { get; set; } = "";
    public string BlockedUserId { get; set; } = "";
}

public sealed class SharedPost
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string TrackJson { get; set; } = "[]";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class IntegrationGrant
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = "";
    public string Application { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string ScopesJson { get; set; } = "[]";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
