using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class SingleActivePassportTests
{
    private static async Task<DashboardDto> Dashboard(HttpClient client, string profile = "personal") =>
        (await client.GetFromJsonAsync<DashboardDto>($"/api/v1/profiles/{profile}/dashboard"))!;

    [Fact]
    public async Task Replacing_requires_confirmation_and_preserves_the_previous_passport()
    {
        using var app = new LocalAppFactory(demoEnabled: false); using var client = app.CreateLocalClient();
        var date = (await Dashboard(client)).Today;
        (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{date:yyyy-MM-dd}", new { steps = 5000 })).EnsureSuccessStatusCode();
        var before = await Dashboard(client); Assert.NotNull(before.CurrentJourney);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "hokkaido" })).StatusCode);
        Assert.Equal("tokyo", (await Dashboard(client)).CurrentJourney!.RouteId);
        (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "hokkaido", replacingRouteId = "tokyo" })).EnsureSuccessStatusCode();
        var after = await Dashboard(client);
        Assert.Equal("single-active", after.JourneyPolicy); Assert.Equal("hokkaido", after.CurrentJourney!.RouteId);
        Assert.Single(after.Journeys, x => x.IsActive);
        var saved = after.Journeys.Single(x => x.RouteId == "tokyo"); Assert.False(saved.IsActive); Assert.NotNull(saved.LeftAtUtc);
        Assert.Equal(before.CurrentJourney.StartedOn, saved.StartedOn); Assert.Equal(before.CurrentJourney.Visits, saved.Visits);
        Assert.Equal(before.CurrentJourney.Steps, saved.Steps); Assert.Equal(before.CurrentJourney.DistanceKm, saved.DistanceKm);
        Assert.Equal(before.History, after.History); Assert.Equal(before.Totals, after.Totals);
    }

    [Fact]
    public async Task Leaving_the_last_passport_is_idempotent_and_does_not_restart_it_on_boot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "trailquest-left-" + Guid.NewGuid().ToString("N"));
        try
        {
            IReadOnlyList<CheckpointVisitDto> visits;
            using (var app = new LocalAppFactory(directory, demoEnabled: false))
            using (var client = app.CreateLocalClient())
            {
                var today = (await Dashboard(client)).Today;
                (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 5000 })).EnsureSuccessStatusCode();
                visits = (await Dashboard(client)).CurrentJourney!.Visits;
                (await client.DeleteAsync("/api/v1/profiles/personal/journeys/tokyo")).EnsureSuccessStatusCode();
                (await client.DeleteAsync("/api/v1/profiles/personal/journeys/tokyo")).EnsureSuccessStatusCode();
                (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 20000 })).EnsureSuccessStatusCode();
                var left = await Dashboard(client); Assert.Null(left.CurrentJourney);
                Assert.Equal(5000, Assert.Single(left.Journeys).Steps); Assert.Equal(visits, left.Journeys[0].Visits);
                Assert.Equal(20000, left.Totals.Steps);
                Assert.Equal("", (await client.GetFromJsonAsync<ExportDto>("/api/v1/profiles/personal/export"))!.SelectedRouteId);
            }
            using var restarted = new LocalAppFactory(directory, demoEnabled: false); using var signedIn = restarted.CreateLocalClient();
            var persisted = await Dashboard(signedIn); Assert.Null(persisted.CurrentJourney);
            Assert.Equal(5000, Assert.Single(persisted.Journeys).Steps); Assert.Equal(visits, persisted.Journeys[0].Visits);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Resume_keeps_bank_and_start_date_without_counting_inactive_days()
    {
        using var app = new LocalAppFactory(demoEnabled: false); using var client = app.CreateLocalClient();
        var originalDate = (await Dashboard(client)).Today;
        async Task Save(int steps) => (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{(await Dashboard(client)).Today:yyyy-MM-dd}", new { steps })).EnsureSuccessStatusCode();
        async Task NextDay()
        {
            app.Clock.Now = app.Clock.Now.AddDays(1);
            (await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = LocalAppFactory.FixturePassword })).EnsureSuccessStatusCode();
        }
        await Save(5000); (await client.DeleteAsync("/api/v1/profiles/personal/journeys/tokyo")).EnsureSuccessStatusCode();
        await NextDay(); await Save(8000);
        await NextDay(); await Save(1000);
        (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "tokyo" })).EnsureSuccessStatusCode();
        var resumed = (await Dashboard(client)).CurrentJourney!;
        Assert.Equal(originalDate, resumed.StartedOn); Assert.Equal(originalDate.AddDays(2), resumed.CountingFrom); Assert.Equal(5000, resumed.Steps);
        await Save(3000); Assert.Equal(7000, (await Dashboard(client)).CurrentJourney!.Steps);
        (await client.DeleteAsync("/api/v1/profiles/personal/journeys/tokyo")).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "tokyo" })).EnsureSuccessStatusCode();
        await Save(500); Assert.Equal(7000, (await Dashboard(client)).CurrentJourney!.Steps);
        await NextDay(); await Save(1000);
        Assert.Equal(8000, (await Dashboard(client)).CurrentJourney!.Steps);
    }

    [Fact]
    public async Task Concurrent_switches_cannot_silently_replace_a_newer_confirmation()
    {
        using var app = new LocalAppFactory(demoEnabled: false); using var client = app.CreateLocalClient();
        var responses = await Task.WhenAll(new[] { "hokkaido", "mistvale" }.Select(route => client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = route, replacingRouteId = "tokyo" })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var result = await Dashboard(client); Assert.Single(result.Journeys, x => x.IsActive); Assert.Equal(2, result.Journeys.Count);
    }

    [Fact]
    public async Task Personal_demo_and_other_users_have_independent_active_passports()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient(); using var member = await app.CreateMemberAsync(owner, "passport-member");
        (await owner.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "hokkaido", replacingRouteId = "tokyo" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await member.DeleteAsync("/api/v1/profiles/personal/journeys/hokkaido")).StatusCode);
        (await member.DeleteAsync("/api/v1/profiles/personal/journeys/tokyo")).EnsureSuccessStatusCode();
        Assert.Equal("hokkaido", (await Dashboard(owner)).CurrentJourney!.RouteId); Assert.Null((await Dashboard(member)).CurrentJourney);
        Assert.Equal("tokyo", (await Dashboard(owner, "demo")).CurrentJourney!.RouteId);
        Assert.Single((await Dashboard(owner, "demo")).Journeys, x => x.IsActive);
    }

    [Fact]
    public async Task Filtered_unique_index_also_rejects_a_second_active_row()
    {
        using var app = new LocalAppFactory(demoEnabled: false); using var client = app.CreateLocalClient();
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        var profile = await db.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
        db.Journeys.Add(new Journey { ProfileId = profile.Id, RouteId = "hokkaido", StartedOn = new(2026, 11, 9), CreatedAtUtc = app.Clock.Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Legacy_multi_passport_upgrade_keeps_the_oldest_and_preserves_all_history()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "trailquest-single-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            DashboardDto original; Dictionary<string,string> definitions;
            using (var app = new LocalAppFactory(directory, demoEnabled: false))
            using (var client = app.CreateLocalClient())
            {
                var today = (await Dashboard(client)).Today;
                (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 5000 })).EnsureSuccessStatusCode();
                original = await Dashboard(client);
                definitions = app.Services.GetRequiredService<RouteCatalog>().Routes.ToDictionary(x => x.Id, x => JsonSerializer.Serialize(x, RouteCatalog.Json));
            }
            var options = new DbContextOptionsBuilder<TrailQuestDbContext>().UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(directory,"trailquest.db"), Pooling = false, ForeignKeys = true }.ToString()).Options;
            await using (var legacy = new TrailQuestDbContext(options))
            {
                await legacy.GetService<IMigrator>().MigrateAsync("20261110151802_AddActivityTrackPointCount");
                var profile = await legacy.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
                foreach (var (route,created) in new[] { ("hokkaido", "2026-11-08T10:00:00Z"), ("mistvale", "2026-11-07T10:00:00Z") })
                {
                    var edition = JsonSerializer.Deserialize<AdventureRouteDto>(definitions[route], RouteCatalog.Json)!;
                    await legacy.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO journeys (ProfileId,RouteId,StartedOn,CreatedAtUtc,ContentVersion,DefinitionJson) VALUES ({profile.Id},{route},{new DateOnly(2026,11,1)},{DateTimeOffset.Parse(created)},{edition.ContentVersion},{definitions[route]})");
                }
                profile.SelectedRouteId = "hokkaido"; await legacy.SaveChangesAsync();
            }
            using var upgraded = new LocalAppFactory(directory, demoEnabled: false); using var signedIn = upgraded.CreateLocalClient();
            var repaired = await Dashboard(signedIn); Assert.Equal("mistvale", repaired.CurrentJourney!.RouteId);
            Assert.Equal(3, repaired.Journeys.Count); Assert.Single(repaired.Journeys, x => x.IsActive);
            Assert.Equal(original.History, repaired.History); Assert.Equal(original.Settings, repaired.Settings); Assert.Equal(original.Totals, repaired.Totals);
            var oldTokyo = repaired.Journeys.Single(x => x.RouteId == "tokyo");
            Assert.Equal(original.CurrentJourney!.Visits, oldTokyo.Visits); Assert.Equal(original.CurrentJourney.StartedOn, oldTokyo.StartedOn);
            Assert.Equal(original.CurrentJourney.DistanceKm, oldTokyo.DistanceKm); Assert.False(oldTokyo.IsActive);
            (await signedIn.PutAsJsonAsync($"/api/v1/profiles/personal/days/{repaired.Today:yyyy-MM-dd}", new { steps = 20000 })).EnsureSuccessStatusCode();
            Assert.Equal(oldTokyo.DistanceKm, (await Dashboard(signedIn)).Journeys.Single(x => x.RouteId == "tokyo").DistanceKm);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
