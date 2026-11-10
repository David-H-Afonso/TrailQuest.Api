namespace TrailQuest.Api.Contracts;

public sealed record FriendDto(string Id, string UserId, string DisplayName, string Status, bool Incoming);
public sealed record FriendRequest(string Username);
public sealed record ShareRequest(string Kind, string SourceId, string? CheckpointId = null, bool IncludeTrack = false, string ProfileId = "personal");
public sealed record PostDto(string Id, string UserId, string DisplayName, string Kind, string Title, string Summary,
    DateTimeOffset CreatedAtUtc, bool IsOwn, IReadOnlyList<TrackPointDto> Track);
public sealed record CommunityDto(IReadOnlyList<FriendDto> Friends, IReadOnlyList<PostDto> Posts);
public sealed record GrantDto(string Id, string Application, IReadOnlyList<string> Scopes, DateTimeOffset ExpiresAtUtc);
public sealed record CreateGrantRequest(string Application, IReadOnlyList<string> Scopes, int Days);
public sealed record CreatedGrantDto(GrantDto Grant, string Token)
{
    public override string ToString() => "[Redacted one-time application grant]";
}
