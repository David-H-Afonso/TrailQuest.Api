using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Net;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Security;
using TrailQuest.Api.Application;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class FixedClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-09T12:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class StubGarmin : IGarminClient
{
    public Dictionary<string, System.Text.Json.JsonElement> Responses { get; } = [];
    public List<string> DataRequests { get; } = [];
    public Func<Task>? BeforeDataFetch { get; set; }
    public byte[] Original { get; set; } = [];
    public async Task<System.Text.Json.JsonElement> ReadAsync(string apiKey, string scope, string path, CancellationToken cancellationToken)
    {
        DataRequests.Add(path); UsedScopes.Add(scope);
        if (BeforeDataFetch is not null) await BeforeDataFetch();
        return Responses.TryGetValue(path, out var value) ? value.Clone() : System.Text.Json.JsonSerializer.SerializeToElement(Array.Empty<object>());
    }
    public Task<byte[]> DownloadAsync(string apiKey, string scope, long activityId, CancellationToken cancellationToken) => Task.FromResult(Original);
    public bool IsConfigured { get; set; }
    public IReadOnlyList<GarminDay> Days { get; set; } = [];
    public Dictionary<string, IReadOnlyList<GarminDay>> DaysByFixtureKey { get; } = [];
    public List<string> UsedScopes { get; } = [];
    public bool RejectVerification { get; set; }
    public Func<Task>? BeforeFetch { get; set; }
    public Task VerifyAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (RejectVerification) throw new AppException("garmin_auth", 502, "Garmin rejected the fixture credentials.");
        return Task.CompletedTask;
    }
    public void Invalidate(string scope) { }
    public async Task<IReadOnlyList<GarminDay>> GetDaysAsync(string apiKey, string scope, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        UsedScopes.Add(scope);
        if (BeforeFetch is not null) await BeforeFetch();
        return DaysByFixtureKey.GetValueOrDefault(apiKey, Days);
    }
}

public sealed class LocalAppFactory(string? sharedDirectory = null, bool demoEnabled = true, string? publicOrigin = null, string? demoFixturePath = null,
    bool allowLanHttp = false, string? remoteAddress = null, string[]? knownNetworks = null, string? lanHttpOrigin = null) : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = sharedDirectory ?? Path.Combine(Path.GetTempPath(), "opencode", $"trailquest-test-{Guid.NewGuid():N}");
    public StubGarmin Garmin { get; } = new();
    public FixedClock Clock { get; } = new();
    public const string FixturePassword = "Fixture passphrase 97!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DataDirectory);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Data:Directory"] = DataDirectory,
                ["Demo:Enabled"] = demoEnabled.ToString(),
                ["Demo:FixturePath"] = demoFixturePath ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "demo.json"),
                ["Hosting:AllowRemote"] = (publicOrigin is not null).ToString(),
                ["Hosting:PublicOrigin"] = publicOrigin,
                ["Hosting:AllowLanHttp"] = allowLanHttp.ToString(),
                ["Hosting:LanHttpOrigin"] = lanHttpOrigin
            };
            for (var index = 0; index < (knownNetworks?.Length ?? 0); index++) values[$"Hosting:KnownNetworks:{index}"] = knownNetworks![index];
            config.AddInMemoryCollection(values);
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            if (remoteAddress is not null) services.AddSingleton<IStartupFilter>(new SimulatedPeer(IPAddress.Parse(remoteAddress)));
            services.RemoveAll<DbContextOptions<TrailQuestDbContext>>();
            services.RemoveAll<TrailQuestDbContext>();
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(DataDirectory, "trailquest.db"), Pooling = false, ForeignKeys = true
            }.ToString();
            services.AddDbContext<TrailQuestDbContext>(options => options.UseSqlite(connection));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IGarminClient>();
            services.AddSingleton<IGarminClient>(Garmin);
            services.RemoveAll<SecurityStorage>();
            services.AddSingleton(_ => new SecurityStorage(Path.Combine(DataDirectory, "fixture-security")));
        });
    }

    private sealed class SimulatedPeer(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = address;
                return continuation(context);
            });
            next(app);
        };
    }

    public HttpClient CreateLocalClient()
    {
        var client = CreateAnonymousClient();
        var status = client.GetFromJsonAsync<SessionDto>("/api/v1/auth/session").GetAwaiter().GetResult()!;
        var response = status.SetupRequired
            ? client.PostAsJsonAsync("/api/v1/auth/setup", new { username = "fixture-owner", displayName = "Fixture Owner", password = FixturePassword }).GetAwaiter().GetResult()
            : client.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = FixturePassword }).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        if (Garmin.IsConfigured)
            client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-owner-garmin-key" }).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        return client;
    }

    public HttpClient CreateAnonymousClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5187"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-TrailQuest-Client", "local-web");
        return client;
    }

    public async Task<HttpClient> CreateMemberAsync(HttpClient owner, string username)
    {
        (await owner.PostAsJsonAsync("/api/v1/auth/users", new { username, displayName = username, password = FixturePassword })).EnsureSuccessStatusCode();
        var client = CreateAnonymousClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = FixturePassword })).EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && sharedDirectory is null && Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true);
    }
}
