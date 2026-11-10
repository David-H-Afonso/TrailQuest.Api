using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;

namespace TrailQuest.Api.Tests;

public sealed class AdventureApiTests
{
    private static async Task<DashboardDto> Dashboard(HttpClient client, string profile = "personal") =>
        await client.GetFromJsonAsync<DashboardDto>($"/api/v1/profiles/{profile}/dashboard") ?? throw new InvalidOperationException();

    [Fact]
    public async Task Initial_profiles_are_isolated_and_demo_is_explicitly_fictional()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var personal = await Dashboard(client);
        var demo = await Dashboard(client, "demo");
        Assert.Equal(0, personal.Totals.Steps);
        Assert.Empty(personal.History);
        Assert.Equal(13700, demo.Totals.Steps);
        Assert.Equal(9.864, demo.Totals.DistanceKm, 4);
        Assert.All(demo.History, day => Assert.Equal("demo", day.Source));
        Assert.Equal(7, demo.Week.Count);
        Assert.NotNull(demo.CurrentJourney);
        Assert.Equal(4, demo.CurrentJourney.Visits.Count);
        Assert.False(personal.Garmin.Configured);
    }

    [Fact]
    public async Task Saving_the_same_day_replaces_its_total_instead_of_adding_steps()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        var path = $"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}";
        (await client.PutAsJsonAsync(path, new { steps = 2000 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(path, new { steps = 2000 })).EnsureSuccessStatusCode();
        Assert.Equal(2000, (await Dashboard(client)).Totals.Steps);
        (await client.PutAsJsonAsync(path, new { steps = 3500 })).EnsureSuccessStatusCode();
        var result = await Dashboard(client);
        Assert.Equal(3500, result.Totals.Steps);
        Assert.Single(result.History);
        Assert.Equal(2.52, result.Totals.DistanceKm, 4);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(150001)]
    public async Task Invalid_step_totals_do_not_change_data(int steps)
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        var response = await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await Dashboard(client)).Totals.Steps);
    }

    [Fact]
    public async Task Missing_steps_and_future_dates_are_rejected()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        var missing = await client.PutAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var future = await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today.AddDays(1):yyyy-MM-dd}", new { steps = 1000 });
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Empty((await Dashboard(client)).History);
    }

    [Fact]
    public async Task Concurrent_demo_walks_do_not_lose_updates_and_reset_preserves_personal_data()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 1234 })).EnsureSuccessStatusCode();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsJsonAsync("/api/v1/demo/advance", new { })));
        Assert.All(responses, response => response.EnsureSuccessStatusCode());
        Assert.Equal(8800, (await Dashboard(client, "demo")).TodayActivity.Steps);
        (await client.PostAsJsonAsync("/api/v1/demo/reset", new { })).EnsureSuccessStatusCode();
        Assert.Equal(13700, (await Dashboard(client, "demo")).Totals.Steps);
        Assert.Equal(1234, (await Dashboard(client)).Totals.Steps);
    }

    [Fact]
    public async Task Garmin_sync_is_idempotent_uses_reported_meters_and_replaces_manual_totals()
    {
        using var app = new LocalAppFactory();
        app.Garmin.IsConfigured = true;
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 5000 })).EnsureSuccessStatusCode();
        app.Garmin.Days = [new GarminDay(today, 8000, 6123)];
        var firstResponse = await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { startDate = today, endDate = today });
        firstResponse.EnsureSuccessStatusCode();
        Assert.Equal(1, (await firstResponse.Content.ReadFromJsonAsync<SyncResultDto>())!.DaysChanged);
        var secondResponse = await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { startDate = today, endDate = today });
        secondResponse.EnsureSuccessStatusCode();
        Assert.Equal(0, (await secondResponse.Content.ReadFromJsonAsync<SyncResultDto>())!.DaysChanged);
        var result = await Dashboard(client);
        Assert.Equal(8000, result.Totals.Steps);
        Assert.Equal(6.123, result.Totals.DistanceKm, 4);
        Assert.Equal("garmin", result.TodayActivity.Source);
        Assert.False(result.TodayActivity.IsDistanceEstimated);
        Assert.NotNull(result.Garmin.LastSyncAtUtc);
    }

    [Fact]
    public async Task Invalid_Garmin_batches_roll_back_all_changes()
    {
        using var app = new LocalAppFactory();
        app.Garmin.IsConfigured = true;
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        app.Garmin.Days = [new GarminDay(today, 1000, 700), new GarminDay(today.AddDays(1), 2000, 1400)];
        var response = await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { startDate = today, endDate = today });
        Assert.False(response.IsSuccessStatusCode);
        var result = await Dashboard(client);
        Assert.Empty(result.History);
        Assert.Null(result.Garmin.LastSyncAtUtc);
    }

    [Fact]
    public async Task Garmin_requires_configuration_personal_profile_and_at_most_28_days()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/profiles/demo/garmin/sync", new { })).StatusCode);
        var today = (await Dashboard(client)).Today;
        var tooLong = await client.PostAsJsonAsync("/api/v1/profiles/personal/garmin/sync", new { startDate = today.AddDays(-28), endDate = today });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task Step_length_changes_do_not_rewrite_history_and_routes_keep_their_start_dates()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var initial = await Dashboard(client);
        var today = initial.Today;
        (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 1000 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/v1/profiles/personal/settings", new { dailyStepGoal = 7000, stepLengthCm = 80, timeZoneId = "UTC" })).EnsureSuccessStatusCode();
        Assert.Equal(0.72, (await Dashboard(client)).TodayActivity.DistanceKm, 4);
        (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today.AddDays(-1):yyyy-MM-dd}", new { steps = 1000 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "mistvale", startedOn = today.AddDays(-1), replacingRouteId = "tokyo" })).EnsureSuccessStatusCode();
        Assert.Equal(1.52, (await Dashboard(client)).CurrentJourney!.DistanceKm, 4);
        (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "tokyo", replacingRouteId = "mistvale" })).EnsureSuccessStatusCode();
        var result = await Dashboard(client);
        Assert.NotNull(result.CurrentJourney);
        Assert.Equal(today, result.CurrentJourney.StartedOn);
        Assert.Equal(0.72, result.CurrentJourney.DistanceKm, 4);
        Assert.Equal(2, result.Journeys.Count);
        Assert.Single(result.Journeys, x => x.IsActive);
    }

    [Fact]
    public async Task Checkpoint_stamps_and_achievements_survive_a_correction()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var today = (await Dashboard(client)).Today;
        var path = $"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}";
        (await client.PutAsJsonAsync(path, new { steps = 15000 })).EnsureSuccessStatusCode();
        Assert.True((await Dashboard(client)).Achievements.Single(x => x.Id == "ten-km").Unlocked);
        (await client.PutAsJsonAsync(path, new { steps = 0 })).EnsureSuccessStatusCode();
        var corrected = await Dashboard(client);
        Assert.Equal(0, corrected.Totals.Steps);
        Assert.True(corrected.Achievements.Single(x => x.Id == "ten-km").Unlocked);
        Assert.NotNull(corrected.CurrentJourney);
        Assert.Equal(4, corrected.CurrentJourney.Visits.Count);
    }

    [Fact]
    public async Task Unknown_profiles_cross_site_requests_and_headerless_changes_are_rejected()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/profiles/other/dashboard")).StatusCode);
        using var foreignRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/profiles/personal/dashboard");
        foreignRequest.Headers.Add("Origin", "https://unrelated.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreignRequest)).StatusCode);
        using var headerless = app.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await headerless.PostAsJsonAsync("/api/v1/demo/advance", new { })).StatusCode);
    }

    [Fact]
    public async Task Export_is_versioned_complete_and_contains_no_credentials()
    {
        using var app = new LocalAppFactory();
        using var client = app.CreateLocalClient();
        var response = await client.GetAsync("/api/v1/profiles/demo/export");
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(text);
        Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(7, json.RootElement.GetProperty("days").GetArrayLength());
        Assert.Equal("tokyo", json.RootElement.GetProperty("selectedRouteId").GetString());
        Assert.DoesNotContain("apiKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task SQLite_data_survives_a_host_restart_without_reseeding_personal_data()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", $"trailquest-restart-test-{Guid.NewGuid():N}");
        try
        {
            using (var first = new LocalAppFactory(directory))
            using (var client = first.CreateLocalClient())
            {
                var today = (await Dashboard(client)).Today;
                (await client.PutAsJsonAsync($"/api/v1/profiles/personal/days/{today:yyyy-MM-dd}", new { steps = 4321 })).EnsureSuccessStatusCode();
            }
            using (var second = new LocalAppFactory(directory))
            using (var client = second.CreateLocalClient())
                Assert.Equal(4321, (await Dashboard(client)).Totals.Steps);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
