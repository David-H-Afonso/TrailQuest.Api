using System.Text.Json;
namespace TrailQuest.Api.Application;

public sealed record GarminDay(DateOnly Date, int Steps, double? DistanceMeters);

public interface IGarminGateway
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);
    Task<string?> GetVersionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GarminDay>> GetDaysAsync(DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
}

public interface IGarminClient
{
    Task VerifyAsync(string apiKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<GarminDay>> GetDaysAsync(string apiKey, string scope, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);
    void Invalidate(string scope);
    Task<JsonElement> ReadAsync(string apiKey, string scope, string path, CancellationToken cancellationToken) =>
        throw new AppException("garmin_unsupported", 502, "This Garmin client does not support additional datasets.");
    Task<byte[]> DownloadAsync(string apiKey, string scope, long activityId, CancellationToken cancellationToken) =>
        throw new AppException("garmin_unsupported", 502, "This Garmin client does not support original files.");
}
