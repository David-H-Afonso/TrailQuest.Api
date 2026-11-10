using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class AccountSecurityTests
{
    [Theory]
    [InlineData("/api/v1/profiles/personal/dashboard")]
    [InlineData("/api/v1/profiles/demo/dashboard")]
    [InlineData("/api/v1/profiles/personal/export")]
    [InlineData("/api/v1/connections/garmin")]
    [InlineData("/api/v1/auth/users")]
    public async Task Sensitive_reads_require_a_real_session(string path)
    {
        using var app = new LocalAppFactory();
        using var anonymous = app.CreateAnonymousClient();
        var response = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("authentication_required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Only_the_first_account_can_claim_legacy_data_and_only_owner_can_create_members()
    {
        using var app = new LocalAppFactory();
        using var owner = app.CreateLocalClient();
        using var member = await app.CreateMemberAsync(owner, "member-one");
        using var anonymous = app.CreateAnonymousClient();
        var retry = await anonymous.PostAsJsonAsync("/api/v1/auth/setup", new { username = "takeover", displayName = "Takeover", password = LocalAppFactory.FixturePassword });
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/auth/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/v1/auth/users", new { username = "extra-user", displayName = "Extra", password = LocalAppFactory.FixturePassword, isOwner = true })).StatusCode);
        var session = await member.GetFromJsonAsync<SessionDto>("/api/v1/auth/session");
        Assert.False(session!.User!.IsOwner);
        var users = await owner.GetFromJsonAsync<UserDto[]>("/api/v1/auth/users");
        Assert.Equal(2, users!.Length);
        Assert.Single(users, x => x.IsOwner);
    }

    [Fact]
    public async Task Every_profile_write_and_export_stays_with_its_authenticated_user()
    {
        using var app = new LocalAppFactory();
        using var owner = app.CreateLocalClient();
        using var member = await app.CreateMemberAsync(owner, "member-two");
        var original = await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard");
        var date = original!.Today;
        (await owner.PutAsJsonAsync($"/api/v1/profiles/personal/days/{date:yyyy-MM-dd}", new { steps = 7777 })).EnsureSuccessStatusCode();
        (await member.PutAsJsonAsync($"/api/v1/profiles/personal/days/{date:yyyy-MM-dd}", new { steps = 1111, userId = "fixture-owner" })).EnsureSuccessStatusCode();
        (await member.PutAsJsonAsync("/api/v1/profiles/personal/settings", new { dailyStepGoal = 6000, stepLengthCm = 80, timeZoneId = "UTC" })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "mistvale", replacingRouteId = "tokyo" })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/v1/demo/advance", new { })).EnsureSuccessStatusCode();
        (await member.PostAsJsonAsync("/api/v1/demo/reset", new { })).EnsureSuccessStatusCode();
        var a = await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard");
        var b = await member.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard?userId=fixture-owner");
        Assert.Equal(7777, a!.Totals.Steps);
        Assert.Equal(8000, a.Settings.DailyStepGoal);
        Assert.NotNull(a.CurrentJourney);
        Assert.Equal("tokyo", a.CurrentJourney.RouteId);
        Assert.Single(a.Journeys);
        Assert.Equal(1111, b!.Totals.Steps);
        Assert.Equal(6000, b.Settings.DailyStepGoal);
        Assert.NotNull(b.CurrentJourney);
        Assert.Equal("mistvale", b.CurrentJourney.RouteId);
        var export = await member.GetFromJsonAsync<ExportDto>("/api/v1/profiles/personal/export");
        Assert.Equal(1111, Assert.Single(export!.Days).Steps);
        var ownerDemo = await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/demo/dashboard");
        Assert.Equal(13700, ownerDemo!.Totals.Steps);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        var ownerId = (await owner.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User!.Id;
        var foreignProfileId = await db.Profiles.Where(x => x.UserId != ownerId && x.Kind == "personal").Select(x => x.Id).SingleAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/profiles/{foreignProfileId}/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Cookie_flags_csrf_checks_and_logout_revocation_are_enforced()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/setup", new { username = "cookie-owner", displayName = "Cookie Owner", password = LocalAppFactory.FixturePassword });
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Domain=", cookie, StringComparison.OrdinalIgnoreCase);
        var ticket = cookie.Split(';')[0];
        client.DefaultRequestHeaders.Remove("X-TrailQuest-Client");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/demo/reset", new { })).StatusCode);
        client.DefaultRequestHeaders.Add("X-TrailQuest-Client", "local-web");
        using var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/v1/demo/reset") { Content = JsonContent.Create(new { }) };
        crossSite.Headers.Add("Origin", "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(crossSite)).StatusCode);
        (await client.PostAsJsonAsync("/api/v1/auth/logout", new { })).EnsureSuccessStatusCode();
        using var replay = app.CreateAnonymousClient();
        replay.DefaultRequestHeaders.Add("Cookie", ticket);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Expired_and_tampered_sessions_are_rejected()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        app.Clock.Now = app.Clock.Now.AddHours(9);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
        using var tampered = app.CreateAnonymousClient();
        tampered.DefaultRequestHeaders.Add("Cookie", "trailquest.session=fixture-invalid-ticket");
        Assert.Equal(HttpStatusCode.Unauthorized, (await tampered.GetAsync("/api/v1/profiles/personal/export")).StatusCode);
    }

    [Fact]
    public async Task Password_change_revokes_all_sessions_and_hashes_are_not_returned()
    {
        using var app = new LocalAppFactory();
        using var first = app.CreateLocalClient();
        using var second = app.CreateLocalClient();
        const string replacement = "Another fixture passphrase 81!";
        (await first.PostAsJsonAsync("/api/v1/auth/password", new { currentPassword = LocalAppFactory.FixturePassword, newPassword = replacement })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
        using var login = app.CreateAnonymousClient();
        (await login.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = replacement })).EnsureSuccessStatusCode();
        var text = await login.GetStringAsync("/api/v1/auth/session");
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(replacement, text);
        await using var scope = app.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>().Users.Select(x => x.PasswordHash).SingleAsync();
        Assert.NotEqual(replacement, stored);
        Assert.NotEqual(LocalAppFactory.FixturePassword, stored);
    }

    [Fact]
    public async Task Login_is_generic_rate_limited_and_accounts_lock_after_failed_attempts()
    {
        using var app = new LocalAppFactory();
        using var owner = app.CreateLocalClient();
        using var anonymous = app.CreateAnonymousClient();
        for (var index = 0; index < 5; index++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = "wrong fixture password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword })).StatusCode);
        var limited = false;
        for (var index = 0; index < 8; index++)
        {
            var response = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { username = "' OR '1'='1", password = LocalAppFactory.FixturePassword });
            limited |= response.StatusCode == HttpStatusCode.TooManyRequests;
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests);
        }
        Assert.True(limited);
    }
}
