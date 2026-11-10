using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TrailQuest.Api.Application;

namespace TrailQuest.Api.Infrastructure.Garmin;

public sealed partial class GarminGateway
{
    public Task<JsonElement> ReadAsync(string apiKey, string scope, string path, CancellationToken cancellationToken) =>
        SafeAsync(async () =>
        {
            var bytes = await FetchAsync(apiKey, scope, path, 8 * 1024 * 1024, cancellationToken);
            if (bytes.Length == 0) return JsonSerializer.SerializeToElement(Array.Empty<object>());
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null))
                throw new AppException("garmin_response", 502, "Garmin returned an unsupported data response.");
            return document.RootElement.Clone();
        }, cancellationToken);

    public Task<byte[]> DownloadAsync(string apiKey, string scope, long activityId, CancellationToken cancellationToken) =>
        SafeAsync(() => activityId > 0
            ? FetchAsync(apiKey, scope, $"/download-service/files/activity/{activityId}", 32 * 1024 * 1024, cancellationToken)
            : throw new AppException("invalid_activity", 400, "Invalid activity."), cancellationToken);

    private async Task<byte[]> FetchAsync(string apiKey, string scope, string path, int maximumBytes, CancellationToken cancellationToken)
    {
        // All callers construct paths from the fixed official endpoint inventory, never browser-supplied URLs.
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('#'))
            throw new InvalidOperationException("Only fixed Garmin paths are allowed.");
        using var client = clientFactory.CreateClient("garmin");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var boundedToken = timeout.Token;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(client, apiKey, scope, attempt == 1, boundedToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(maximumBytes == 8 * 1024 * 1024 ? "application/json" : "*/*"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, boundedToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            EnsureSuccess(response.StatusCode);
            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new AppException("garmin_size", 502, "The Garmin response exceeds the safe import limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(boundedToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, boundedToken)) > 0)
            {
                if (buffer.Length + read > maximumBytes) throw new AppException("garmin_size", 502, "The Garmin response exceeds the safe import limit.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        throw new AppException("garmin_auth", 502, "Garmin rejected the connection. Reconnect in Settings.");
    }
}
