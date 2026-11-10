namespace TrailQuest.Api.Infrastructure.Operations;

public static class HealthProbe
{
    public static bool IsStatusPath(PathString path) => path == "/health/live" || path == "/health/ready";

    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var port = configuration.GetValue("Health:Port", 8080);
        if (port is < 1024 or > 65535) return 1;
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/health/ready");
        if (configuration.GetValue("Hosting:AllowRemote", false) &&
            Uri.TryCreate(configuration["Hosting:PublicOrigin"], UriKind.Absolute, out var origin))
            request.Headers.Host = origin.Authority;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return response.StatusCode == System.Net.HttpStatusCode.OK ? 0 : 1;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { return 1; }
    }
}
