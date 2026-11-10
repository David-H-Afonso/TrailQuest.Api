using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class GarminConnectionSecurityTests
{
    [Fact]
    public async Task Credentials_are_encrypted_never_returned_and_scoped_to_the_signed_in_user()
    {
        using var app = new LocalAppFactory();
        using var a = app.CreateLocalClient();
        using var b = await app.CreateMemberAsync(a, "garmin-member");
        const string keyA = "fixture-garmin-key-owner";
        const string keyB = "fixture-garmin-key-member";
        var date = (await a.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Today;
        app.Garmin.DaysByFixtureKey[keyA] = [new GarminDay(date, 2000, 1300)];
        app.Garmin.DaysByFixtureKey[keyB] = [new GarminDay(date, 9000, 6700)];
        (await a.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = keyA })).EnsureSuccessStatusCode();
        Assert.False((await b.GetFromJsonAsync<GarminConnectionDto>("/api/v1/connections/garmin"))!.Configured);
        (await b.PutAsJsonAsync("/api/v1/connections/garmin?userId=fixture-owner", new { apiKey = keyB, userId = "fixture-owner" })).EnsureSuccessStatusCode();
        foreach (var client in new[] { a, b }) (await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { })).EnsureSuccessStatusCode();
        Assert.Equal(2000, (await a.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
        Assert.Equal(9000, (await b.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
        Assert.Equal(2, app.Garmin.UsedScopes.Distinct().Count());
        foreach (var path in new[] { "/api/v1/connections/garmin", "/api/v1/profiles/personal/dashboard", "/api/v1/profiles/personal/export", "/api/v1/auth/users" })
        {
            var text = await a.GetStringAsync(path);
            Assert.DoesNotContain(keyA, text);
            Assert.DoesNotContain(keyB, text);
            Assert.DoesNotContain("protectedApiKey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        }
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        var stored = await db.GarminConnections.Select(x => x.ProtectedApiKey).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, encrypted => { Assert.DoesNotContain(keyA, encrypted); Assert.DoesNotContain(keyB, encrypted); });
        (await b.DeleteAsync("/api/v1/connections/garmin")).EnsureSuccessStatusCode();
        Assert.True((await a.GetFromJsonAsync<GarminConnectionDto>("/api/v1/connections/garmin"))!.Configured);
        Assert.False((await b.GetFromJsonAsync<GarminConnectionDto>("/api/v1/connections/garmin"))!.Configured);
        Assert.Equal(9000, (await b.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
    }

    [Fact]
    public async Task A_rejected_replacement_does_not_destroy_a_working_connection()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        (await client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-working-garmin-key" })).EnsureSuccessStatusCode();
        app.Garmin.RejectVerification = true;
        var failed = await client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-rejected-garmin-key" });
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.True((await client.GetFromJsonAsync<GarminConnectionDto>("/api/v1/connections/garmin"))!.Configured);
        Assert.DoesNotContain("fixture-rejected-garmin-key", await failed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_connection_change_during_sync_never_imports_stale_account_data()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var date = (await client.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Today;
        app.Garmin.Days = [new GarminDay(date, 9999, 6500)];
        (await client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-original-garmin-key" })).EnsureSuccessStatusCode();
        app.Garmin.BeforeFetch = async () => (await client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-replacement-garmin-key" })).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
    }

    [Fact]
    public async Task Ciphertext_from_another_user_cannot_be_decrypted_even_if_the_fixture_database_is_tampered()
    {
        using var app = new LocalAppFactory();
        using var a = app.CreateLocalClient();
        using var b = await app.CreateMemberAsync(a, "purpose-member");
        (await a.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-owner-purpose-key" })).EnsureSuccessStatusCode();
        (await b.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-member-purpose-key" })).EnsureSuccessStatusCode();
        var aId = (await a.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User!.Id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var records = await db.GarminConnections.ToListAsync();
            records.Single(x => x.UserId != aId).ProtectedApiKey = records.Single(x => x.UserId == aId).ProtectedApiKey;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await b.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { })).StatusCode);
    }

    [Fact]
    public async Task Keys_survive_backend_restart_without_becoming_global()
    {
        var directory = Path.Combine("/tmp/opencode", $"trailquest-key-restart-{Guid.NewGuid():N}");
        try
        {
            using (var first = new LocalAppFactory(directory))
            using (var client = first.CreateLocalClient())
                (await client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-persistent-garmin-key" })).EnsureSuccessStatusCode();
            using var second = new LocalAppFactory(directory);
            using var owner = second.CreateLocalClient();
            Assert.True((await owner.GetFromJsonAsync<GarminConnectionDto>("/api/v1/connections/garmin"))!.Configured);
            using var member = await second.CreateMemberAsync(owner, "restart-member");
            Assert.False((await member.GetFromJsonAsync<GarminConnectionDto>("/api/v1/connections/garmin"))!.Configured);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
