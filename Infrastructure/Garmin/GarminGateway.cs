using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TrailQuest.Api.Application;

namespace TrailQuest.Api.Infrastructure.Garmin;

public sealed partial class GarminGateway(IHttpClientFactory clientFactory, TimeProvider clock) : IGarminClient
{
    private sealed class TokenSlot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? AccessToken { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
    private readonly ConcurrentDictionary<string, TokenSlot> tokens = new();

    public void Invalidate(string scope) => tokens.TryRemove(scope, out _);

    public async Task VerifyAsync(string apiKey, CancellationToken cancellationToken)
    {
        var scope = "verification-" + Guid.NewGuid().ToString("N");
        try
        {
            await SafeAsync(async () =>
            {
                using var client = clientFactory.CreateClient("garmin");
                await GetTokenAsync(client, apiKey, scope, false, cancellationToken);
                return true;
            }, cancellationToken);
        }
        finally { Invalidate(scope); }
    }

    public Task<IReadOnlyList<GarminDay>> GetDaysAsync(
        string apiKey, string scope, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
        => SafeAsync<IReadOnlyList<GarminDay>>(async () =>
    {
        using var client = clientFactory.CreateClient("garmin");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(client, apiKey, scope, attempt == 1, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/usersummary-service/stats/steps/daily/{startDate:yyyy-MM-dd}/{endDate:yyyy-MM-dd}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            EnsureSuccess(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ParseDays(document.RootElement, startDate, endDate);
        }
        throw new AppException("garmin_auth", 502, "Garmin rejected the credentials.");
    }, cancellationToken);

    private static async Task<T> SafeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try { return await action(); }
        catch (AppException) { throw; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppException("garmin_timeout", 504, "Garmin did not respond in time.");
        }
        catch (HttpRequestException)
        {
            throw new AppException("garmin_network", 502, "The local backend could not reach Garmin.");
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or OverflowException or ArgumentOutOfRangeException)
        {
            throw new AppException("garmin_response", 502, "Garmin returned an unsupported response. No data was changed.");
        }
    }

    private async Task<string> GetTokenAsync(HttpClient client, string apiKey, string scope, bool forceRefresh, CancellationToken cancellationToken)
    {
        var slot = tokens.GetOrAdd(scope, _ => new TokenSlot());
        await slot.Gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && slot.AccessToken is not null && slot.ExpiresAtUtc > clock.GetUtcNow().AddMinutes(5))
                return slot.AccessToken;
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/personalapitoken-service/personal-api/personal-api-key/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            EnsureSuccess(response.StatusCode);

            // Garmin returns a plain-text JWT, not an OAuth JSON envelope.
            var token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var parts = token.Split('.');
            if (parts.Length != 3 || token.Length > 16384)
                throw new AppException("garmin_response", 502, "Garmin returned an unsupported token response.");
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!document.RootElement.TryGetProperty("exp", out var expiration) || !expiration.TryGetInt64(out var seconds))
                throw new AppException("garmin_response", 502, "Garmin returned a token without an expiration.");
            slot.ExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (slot.ExpiresAtUtc <= clock.GetUtcNow())
                throw new AppException("garmin_auth", 502, "Garmin returned an expired token.");
            slot.AccessToken = token;
            return token;
        }
        finally { slot.Gate.Release(); }
    }

    public static IReadOnlyList<GarminDay> ParseDays(JsonElement root, DateOnly startDate, DateOnly endDate)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new AppException("garmin_response", 502, "Garmin returned an unsupported daily steps response.");
        var days = new List<GarminDay>();
        var dates = new HashSet<DateOnly>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("calendarDate", out var dateValue) || dateValue.ValueKind != JsonValueKind.String ||
                !DateOnly.TryParseExact(dateValue.GetString(), "yyyy-MM-dd", out var date) ||
                date < startDate || date > endDate || !dates.Add(date) ||
                !item.TryGetProperty("totalSteps", out var stepsValue) || stepsValue.ValueKind != JsonValueKind.Number || !stepsValue.TryGetInt32(out var steps) ||
                steps is < 0 or > 150000)
                throw new AppException("garmin_response", 502, "Garmin returned invalid or duplicate daily steps. No data was changed.");

            double? distance = null;
            if (item.TryGetProperty("totalDistance", out var distanceValue) && distanceValue.ValueKind != JsonValueKind.Null)
            {
                if (distanceValue.ValueKind != JsonValueKind.Number || !distanceValue.TryGetDouble(out var meters) || !double.IsFinite(meters) || meters is < 0 or > 500000)
                    throw new AppException("garmin_response", 502, "Garmin returned an invalid daily distance. No data was changed.");
                if (meters > 0 || steps == 0) distance = meters;
            }
            days.Add(new GarminDay(date, steps, distance));
        }
        return days;
    }

    private static void EnsureSuccess(HttpStatusCode status)
    {
        if ((int)status is >= 200 and < 300) return;
        throw status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new AppException("garmin_auth", 502, "Garmin rejected the credentials. Check the key and its expiration."),
            HttpStatusCode.TooManyRequests =>
                new AppException("garmin_rate_limit", 429, "Garmin has temporarily limited requests. Try again later."),
            _ => new AppException("garmin_unavailable", 502, "Garmin is temporarily unavailable. No data was changed.")
        };
    }
}
