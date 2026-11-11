using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Security;

namespace TrailQuest.Api.Tests;

public sealed class LanHostingTests
{
    private const string LanOrigin = "http://192.168.0.32:5223";
    private const string HttpsOrigin = "https://trailquest.example";

    private static HostingPolicy Policy(string origin, bool allowLanHttp, string? network = null, string? lanOrigin = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Hosting:AllowRemote"] = "true", ["Hosting:PublicOrigin"] = origin,
            ["Hosting:AllowLanHttp"] = allowLanHttp.ToString(), ["Hosting:LanHttpOrigin"] = lanOrigin
        };
        if (network is not null) values["Hosting:KnownNetworks:0"] = network;
        return new HostingPolicy(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Theory]
    [InlineData("http://192.168.0.32:5223", false)]
    [InlineData("http://8.8.8.8:5223", true)]
    [InlineData("http://0.0.0.0:5223", true)]
    [InlineData("http://localhost:5223", true)]
    [InlineData("http://trailquest.example:5223", true)]
    [InlineData("http://192.168.0.32:5223/path", true)]
    [InlineData("http://user:password@192.168.0.32:5223", true)]
    public void Http_requires_explicit_opt_in_and_an_unambiguous_private_ip(string origin, bool optIn) =>
        Assert.Throws<InvalidOperationException>(() => Policy(origin, optIn));

    [Theory]
    [InlineData("http://192.168.0.32:5223")]
    [InlineData("http://10.1.2.3:5223")]
    [InlineData("http://172.16.0.5:5223")]
    [InlineData("http://[fd12:3456::1]:5223")]
    public void Explicit_private_lan_origins_are_accepted(string origin)
    {
        var policy = Policy(origin, true);
        Assert.True(policy.LanHttpEnabled);
        Assert.Contains(new Uri(origin).GetLeftPart(UriPartial.Authority), policy.AllowedOrigins);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("10.0.0.0/8")]
    [InlineData("172.16.0.0/16")]
    [InlineData("8.8.8.0/24")]
    [InlineData("::/0")]
    [InlineData("fc00::/7")]
    [InlineData("2001:4860::/64")]
    public void Proxy_networks_cannot_trust_public_or_overly_broad_ranges(string network) =>
        Assert.Throws<InvalidOperationException>(() => Policy("https://trailquest.example", false, network));

    [Theory]
    [InlineData("10.253.52.0/29")]
    [InlineData("fd12:3456::/64")]
    public void Dedicated_private_proxy_networks_are_accepted(string network)
    {
        var policy = Policy("https://trailquest.example", true, network);
        Assert.True(policy.HasTrustedProxies);
        Assert.Single(policy.KnownNetworks);
        Assert.False(policy.LanHttpEnabled);
    }

    [Fact]
    public void Https_and_explicit_private_http_origins_can_coexist()
    {
        var policy = Policy(HttpsOrigin, true, lanOrigin: LanOrigin);
        Assert.Equal(new Uri(HttpsOrigin), policy.PublicOrigin);
        Assert.Equal(new Uri(LanOrigin), policy.LanHttpOrigin);
        Assert.True(policy.LanHttpEnabled);
        Assert.Equal(2, policy.AllowedOrigins.Count);
        Assert.Contains(HttpsOrigin, policy.AllowedOrigins);
        Assert.Contains(LanOrigin, policy.AllowedOrigins);
        Assert.Contains("trailquest.example", policy.AllowedHosts);
        Assert.Contains("192.168.0.32", policy.AllowedHosts);
    }

    [Theory]
    [InlineData("https://trailquest.example", "http://192.168.0.32:5223", false)]
    [InlineData("http://192.168.0.32:5223", "http://192.168.0.33:5223", true)]
    [InlineData("https://trailquest.example", "http://8.8.8.8:5223", true)]
    [InlineData("https://trailquest.example", "http://local.example:5223", true)]
    [InlineData("https://trailquest.example", "https://192.168.0.32:5223", true)]
    [InlineData("https://trailquest.example", "http://192.168.0.32:5223/path", true)]
    [InlineData("https://trailquest.example", "http://user:password@192.168.0.32:5223", true)]
    public void Additional_lan_origin_keeps_explicit_opt_in_and_private_ip_guards(string publicOrigin, string lanOrigin, bool optIn) =>
        Assert.Throws<InvalidOperationException>(() => Policy(publicOrigin, optIn, lanOrigin: lanOrigin));

    private static async Task WithOwner(Func<string, Task> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "trailquest-lan-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var local = new LocalAppFactory(directory)) using (var owner = local.CreateLocalClient()) { }
            await test(directory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static HttpClient Client(LocalAppFactory app, string origin, bool handleCookies = true)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(origin), AllowAutoRedirect = false, HandleCookies = handleCookies });
        client.DefaultRequestHeaders.Add("X-TrailQuest-Client", "local-web");
        client.DefaultRequestHeaders.Add("Origin", origin);
        return client;
    }

    [Fact]
    public Task Lan_login_uses_private_http_cookies_and_preserves_origin_and_client_guards() => WithOwner(async directory =>
    {
        using var app = new LocalAppFactory(directory, publicOrigin: LanOrigin, allowLanHttp: true, remoteAddress: "192.168.0.50");
        using var client = Client(app, LanOrigin);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword });
        login.EnsureSuccessStatusCode();
        var cookie = login.Headers.GetValues("Set-Cookie").Single();
        Assert.Equal("trailquest.lan.session", cookie.Split('=', 2)[0]);
        Assert.True(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.True(cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.False(cookie.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        var attributes = cookie.Split(';').Skip(1).Select(part => part.Trim().Split('=', 2)[0].ToLowerInvariant()).ToArray();
        Assert.DoesNotContain("secure", attributes);
        Assert.False(login.Headers.Contains("Strict-Transport-Security"));
        Assert.NotNull((await client.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        foreign.Headers.Add("Origin", "http://foreign.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
        using var crossSite = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(crossSite)).StatusCode);
        using var anonymous = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(LanOrigin) });
        var missingHeader = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword });
        Assert.Equal(HttpStatusCode.Forbidden, missingHeader.StatusCode);
    });

    [Fact]
    public Task Public_clients_cannot_use_the_lan_http_exception() => WithOwner(async directory =>
    {
        using var app = new LocalAppFactory(directory, publicOrigin: LanOrigin, allowLanHttp: true, remoteAddress: "8.8.8.8");
        using var client = Client(app, LanOrigin);
        var response = await client.GetAsync("/api/v1/demo/status");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("lan_http_only", await response.Content.ReadAsStringAsync());
    });

    [Theory]
    [InlineData("10.253.52.3", true)]
    [InlineData("10.253.52.5", true)]
    [InlineData("172.20.0.3", false)]
    public Task Proxy_trust_survives_dynamic_addresses_but_rejects_other_networks(string proxy, bool trusted) => WithOwner(async directory =>
    {
        const string origin = "https://trailquest.example";
        using var app = new LocalAppFactory(directory, publicOrigin: origin, remoteAddress: proxy, knownNetworks: ["10.253.52.0/29"]);
        using var client = Client(app, origin);
        client.BaseAddress = new Uri("http://trailquest.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "trailquest.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.168.0.50");
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword });
        Assert.Equal(trusted ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (trusted) Assert.True(response.Headers.GetValues("Set-Cookie").Single().Contains("; secure", StringComparison.OrdinalIgnoreCase));
    });

    [Theory]
    [InlineData("192.168.0.50", HttpStatusCode.OK)]
    [InlineData("8.8.8.8", HttpStatusCode.Forbidden)]
    public Task Lan_boundary_uses_the_real_forwarded_client_not_only_the_proxy(string clientAddress, HttpStatusCode expected) => WithOwner(async directory =>
    {
        using var app = new LocalAppFactory(directory, publicOrigin: LanOrigin, allowLanHttp: true, remoteAddress: "10.253.52.3", knownNetworks: ["10.253.52.0/29"]);
        using var client = Client(app, LanOrigin);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "192.168.0.32:5223");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientAddress);
        Assert.Equal(expected, (await client.GetAsync("/api/v1/auth/session")).StatusCode);
    });

    private static LocalAppFactory MixedApp(string directory, string peer = "192.168.0.50", string[]? networks = null) =>
        new(directory, publicOrigin: HttpsOrigin, allowLanHttp: true, remoteAddress: peer, knownNetworks: networks, lanHttpOrigin: LanOrigin);

    private static async Task<string> Login(HttpClient client, bool secure)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword });
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Equal(secure ? "trailquest.session" : "trailquest.lan.session", cookie.Split('=', 2)[0]);
        var attributes = cookie.Split(';').Skip(1).Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("samesite=strict", attributes);
        Assert.DoesNotContain(attributes, part => part.StartsWith("domain=", StringComparison.Ordinal));
        if (secure) Assert.Contains("secure", attributes);
        else Assert.DoesNotContain("secure", attributes);
        Assert.Equal(secure, response.Headers.Contains("Strict-Transport-Security"));
        return cookie.Split(';', 2)[0];
    }

    [Fact]
    public Task Both_origins_support_real_login_private_reads_and_transport_specific_logout_cookies() => WithOwner(async directory =>
    {
        using var app = MixedApp(directory);
        foreach (var origin in new[] { LanOrigin, HttpsOrigin })
        {
            using var client = Client(app, origin);
            var secure = origin == HttpsOrigin;
            await Login(client, secure);
            Assert.NotNull((await client.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
            var logout = await client.PostAsync("/api/v1/auth/logout", null);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Equal(secure ? "trailquest.session" : "trailquest.lan.session", logout.Headers.GetValues("Set-Cookie").Single().Split('=', 2)[0]);
            Assert.Null((await client.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User);
        }
    });

    [Theory]
    [InlineData("http://trailquest.example")]
    [InlineData("https://192.168.0.32:5223")]
    [InlineData("http://192.168.0.32:5224")]
    public Task Mixed_hosting_rejects_unconfigured_scheme_and_port_combinations(string origin) => WithOwner(async directory =>
    {
        using var app = MixedApp(directory);
        using var client = Client(app, origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/auth/session")).StatusCode);
    });

    [Theory]
    [InlineData("https://trailquest.example", "http://192.168.0.32:5223")]
    [InlineData("http://192.168.0.32:5223", "https://trailquest.example")]
    public Task A_registered_origin_does_not_authorize_cross_origin_requests_to_the_other_access(string target, string origin) => WithOwner(async directory =>
    {
        using var app = MixedApp(directory);
        using var client = Client(app, target);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/session");
        request.Headers.Add("Origin", origin);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("origin_rejected", await response.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task Public_https_clients_are_allowed_while_public_lan_http_clients_stay_blocked() => WithOwner(async directory =>
    {
        using var app = MixedApp(directory, "10.253.52.3", ["10.253.52.0/29"]);
        using var https = Client(app, HttpsOrigin);
        https.BaseAddress = new Uri("http://trailquest.example");
        https.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        https.DefaultRequestHeaders.Add("X-Forwarded-Host", "trailquest.example");
        https.DefaultRequestHeaders.Add("X-Forwarded-For", "8.8.8.8");
        await Login(https, true);
        using var http = Client(app, LanOrigin);
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "192.168.0.32:5223");
        http.DefaultRequestHeaders.Add("X-Forwarded-For", "8.8.8.8");
        var response = await http.GetAsync("/api/v1/auth/session");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("lan_http_only", await response.Content.ReadAsStringAsync());
    });

    [Fact]
    public Task Both_sessions_survive_application_recreation_with_preserved_data_and_keys() => WithOwner(async directory =>
    {
        string lanCookie, httpsCookie;
        using (var app = MixedApp(directory))
        {
            using var http = Client(app, LanOrigin);
            using var https = Client(app, HttpsOrigin);
            lanCookie = await Login(http, false);
            httpsCookie = await Login(https, true);
        }
        using var recreated = MixedApp(directory);
        foreach (var (origin, cookie) in new[] { (LanOrigin, lanCookie), (HttpsOrigin, httpsCookie) })
        {
            using var client = Client(recreated, origin, false);
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.NotNull((await client.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User);
        }
        using var wrongTransport = Client(recreated, LanOrigin, false);
        wrongTransport.DefaultRequestHeaders.Add("Cookie", httpsCookie);
        Assert.Null((await wrongTransport.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User);
    });

    [Fact]
    public Task Adding_https_does_not_invalidate_the_existing_lan_session() => WithOwner(async directory =>
    {
        string cookie;
        using (var original = new LocalAppFactory(directory, publicOrigin: LanOrigin, allowLanHttp: true, remoteAddress: "192.168.0.50"))
        {
            using var client = Client(original, LanOrigin);
            cookie = await Login(client, false);
        }
        using var app = MixedApp(directory);
        using var restored = Client(app, LanOrigin, false);
        restored.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.NotNull((await restored.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User);
    });
}
