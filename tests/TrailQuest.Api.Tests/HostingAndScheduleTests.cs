using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Security;

namespace TrailQuest.Api.Tests;

public sealed class HostingAndScheduleTests
{
    [Fact]
    public void Alternate_local_web_port_keeps_an_exact_loopback_origin_allowlist()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Hosting:LocalWebPort"] = "5222" }).Build();
        var policy = new HostingPolicy(config);
        Assert.False(policy.AllowRemote);
        Assert.Contains("http://127.0.0.1:5222", policy.AllowedOrigins);
        Assert.Contains("http://localhost:5222", policy.AllowedOrigins);
        Assert.DoesNotContain("http://127.0.0.1:5186", policy.AllowedOrigins);
        Assert.DoesNotContain("http://other.example:5222", policy.AllowedOrigins);
    }

    [Theory]
    [InlineData("http://trailquest.example")]
    [InlineData("https://trailquest.example/path")]
    [InlineData("https://user:password@trailquest.example")]
    public void Remote_mode_rejects_insecure_or_ambiguous_origins(string origin)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Hosting:AllowRemote"] = "true", ["Hosting:PublicOrigin"] = origin }).Build();
        Assert.Throws<InvalidOperationException>(() => new HostingPolicy(config));
    }

    [Fact]
    public async Task Prepared_remote_mode_requires_HTTPS_exact_origin_and_secure_host_only_cookies()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", $"trailquest-hosting-{Guid.NewGuid():N}");
        try
        {
            using (var local = new LocalAppFactory(directory)) using (var owner = local.CreateLocalClient()) { }
            using var remote = new LocalAppFactory(directory, publicOrigin: "https://trailquest.example");
            Assert.True(remote.Services.GetRequiredService<HostingPolicy>().AllowRemote);
            using var client = remote.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://trailquest.example"), AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-TrailQuest-Client", "local-web");
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword });
            response.EnsureSuccessStatusCode();
            var cookie = response.Headers.GetValues("Set-Cookie").Single();
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Domain=", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
            using var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profiles/personal/dashboard"); foreign.Headers.Add("Origin", "https://other.example");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
            using var insecure = new HttpRequestMessage(HttpMethod.Get, "http://trailquest.example/api/v1/health"); insecure.Headers.Add("X-Forwarded-Proto", "https");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(insecure)).StatusCode);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Scheduled_sync_is_inert_until_opt_in_and_runs_only_the_due_account()
    {
        using var app = new LocalAppFactory(); app.Garmin.IsConfigured = true;
        using var client = app.CreateLocalClient();
        var worker = app.Services.GetServices<IHostedService>().OfType<GarminSyncWorker>().Single();
        await worker.RunDueAsync(default); Assert.Empty(app.Garmin.DataRequests);
        (await client.PutAsJsonAsync("/api/v1/connections/garmin/preferences", new { datasets = new[] { "daily-summary" }, intervalHours = 24, lookbackDays = 1, importOriginals = false })).EnsureSuccessStatusCode();
        await worker.RunDueAsync(default); Assert.Single(app.Garmin.DataRequests);
        await worker.RunDueAsync(default); Assert.Single(app.Garmin.DataRequests);
        app.Clock.Now = app.Clock.Now.AddHours(25);
        await worker.RunDueAsync(default); Assert.Equal(2, app.Garmin.DataRequests.Count);
    }

    [Theory]
    [InlineData("Romance Standard Time", "Europe/Paris")]
    [InlineData("UTC", "Etc/UTC")]
    [InlineData("Europe/Madrid", "Europe/Madrid")]
    public void Browser_contract_uses_IANA_zone_ids_without_rewriting_history(string stored, string expected)
    {
        Assert.Equal(expected, TimeZoneIds.ForClient(stored));
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(TimeZoneIds.ForClient(stored)));
    }
}
