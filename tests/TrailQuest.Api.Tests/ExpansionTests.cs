using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Catalog;
using TrailQuest.Api.Infrastructure.Garmin;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class ExpansionTests
{
    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    private const string Gpx = "<gpx version=\"1.1\"><trk><trkseg><trkpt lat=\"28.45\" lon=\"-16.25\"><time>2026-11-09T08:00:00Z</time></trkpt><trkpt lat=\"28.46\" lon=\"-16.24\"><time>2026-11-09T08:10:00Z</time></trkpt><trkpt lat=\"28.47\" lon=\"-16.23\"><time>2026-11-09T08:20:00Z</time></trkpt></trkseg></trk></gpx>";
    private static async Task<long> Import(HttpClient client, string profile = "personal")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(Gpx)), "file", "synthetic.gpx");
        form.Add(new StringContent("Synthetic outing"), "name");
        var response = await client.PostAsync($"/api/v1/profiles/{profile}/activities/import", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_demo_fixtures_leaves_every_surface_functional_and_personal_empty(bool enabled)
    {
        using var app = new LocalAppFactory(demoEnabled: enabled, demoFixturePath: Path.Combine(Path.GetTempPath(), "opencode", $"absent-demo-{Guid.NewGuid():N}.json"));
        using var client = app.CreateLocalClient();
        foreach (var profile in new[] { "demo", "personal" })
        {
            var dashboard = await client.GetFromJsonAsync<DashboardDto>($"/api/v1/profiles/{profile}/dashboard");
            var health = await client.GetFromJsonAsync<HealthOverviewDto>($"/api/v1/profiles/{profile}/health");
            Assert.Empty(dashboard!.History); Assert.Empty(health!.Activities); Assert.Empty(health.Days);
            Assert.All((await client.GetFromJsonAsync<ChallengeDto[]>($"/api/v1/profiles/{profile}/challenges"))!, x => Assert.Equal(0, x.Value));
        }
        (await client.PostAsJsonAsync("/api/v1/demo/reset", new { })).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<CommunityDto>("/api/v1/community?profile=demo"))!.Posts);
    }

    [Fact]
    public async Task Demo_health_strength_and_tracks_use_real_models_without_creating_fake_people()
    {
        using var app = new LocalAppFactory(); using var client = app.CreateLocalClient();
        var demo = await client.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/demo/health");
        Assert.Equal(4, demo!.Activities.Count); Assert.All(demo.Activities, x => Assert.Equal("demo", x.Source));
        Assert.Contains(demo.Days, x => x.Series.Any(s => s.Id == "heart-rate" && s.Samples.Count > 1));
        Assert.Contains(demo.Days.SelectMany(x => x.Metrics), x => x.Id == "sleep-hours" && x.Value == 7.4);
        var challenges = await client.GetFromJsonAsync<ChallengeDto[]>("/api/v1/profiles/demo/challenges");
        Assert.Equal(9100, challenges!.Single(x => x.Metric == "strength-volume").Value);
        Assert.Equal(3, challenges!.Single(x => x.Metric == "strength-sessions").Value);
        Assert.Single((await client.GetFromJsonAsync<UserDto[]>("/api/v1/auth/users"))!);
        Assert.Empty((await client.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health"))!.Days);
    }

    [Fact]
    public async Task Content_packs_are_generic_and_started_journeys_hold_their_own_definition()
    {
        using var app = new LocalAppFactory(); using var client = app.CreateLocalClient();
        var catalog = await client.GetFromJsonAsync<AdventureRouteDto[]>("/api/v1/routes");
        Assert.Contains(catalog!, x => x.Id == "hokkaido");
        Assert.All(catalog!.SelectMany(x => x.Checkpoints), x => { Assert.Empty(x.Story); Assert.Empty(x.Description); });
        (await client.PostAsJsonAsync("/api/v1/profiles/personal/journeys", new { routeId = "hokkaido", replacingRouteId = "tokyo" })).EnsureSuccessStatusCode();
        var dashboard = await client.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard");
        Assert.NotNull(dashboard!.CurrentJourney);
        Assert.Equal(catalog!.Single(x => x.Id == "hokkaido").ContentVersion, dashboard.CurrentJourney.ContentVersion);
        Assert.Equal("Hokkaidō", dashboard.CurrentJourney.Definition!.Passport.Title);
        Assert.NotEmpty(dashboard.CurrentJourney.Definition.Checkpoints[0].Story);
        Assert.Empty(dashboard.CurrentJourney.Definition.Checkpoints[1].Story);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        Assert.All(await db.Journeys.ToListAsync(), x => Assert.NotEmpty(x.DefinitionJson));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/content/tokyo/not-registered.svg")).StatusCode);
    }

    [Fact]
    public async Task Gpx_import_is_idempotent_user_scoped_and_never_adds_daily_steps()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        using var member = await app.CreateMemberAsync(owner, "track-member");
        var id = await Import(owner); Assert.Equal(id, await Import(owner));
        var overview = await owner.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health");
        Assert.Single(overview!.Activities);
        var detail = await owner.GetFromJsonAsync<ActivityDetailDto>($"/api/v1/profiles/personal/activities/{id}");
        Assert.Equal(3, detail!.Track.Count); Assert.Equal(1200, detail.Activity.DurationSeconds);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/profiles/personal/activities/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/profiles/personal/activities/{id}/original")).StatusCode);
        Assert.Equal(0, (await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
    }

    [Fact]
    public async Task Untimed_courses_do_not_become_fabricated_completed_activities()
    {
        using var app = new LocalAppFactory(); using var client = app.CreateLocalClient();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("<gpx><trk><trkseg><trkpt lat=\"28.45\" lon=\"-16.25\"/><trkpt lat=\"28.46\" lon=\"-16.24\"/></trkseg></trk></gpx>")), "file", "planned-course.gpx");
        var response = await client.PostAsync("/api/v1/profiles/personal/activities/import", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("activity_time_missing", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty((await client.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health"))!.Activities);
    }

    [Fact]
    public async Task Full_sync_preserves_unknown_fields_and_is_idempotent_without_network_on_reads()
    {
        using var app = new LocalAppFactory(); app.Garmin.IsConfigured = true;
        app.Garmin.Responses["/usersummary-service/v2/dailysummaries?startDate=2026-11-09&endDate=2026-11-09"] = Json("{\"data\":[{\"calendarDate\":\"2026-11-09\",\"calories\":{\"burnedActive\":321},\"futureMetric\":{\"unmodeledValue\":7}}],\"futureEnvelope\":42}");
        using var client = app.CreateLocalClient();
        (await client.PutAsJsonAsync("/api/v1/connections/garmin/preferences", new { datasets = new[] { "daily-summary" }, intervalHours = 0, lookbackDays = 1, importOriginals = false })).EnsureSuccessStatusCode();
        for (var i = 0; i < 2; i++) (await client.PostAsJsonAsync("/api/v1/connections/garmin/sync", new { startDate = "2026-11-09", endDate = "2026-11-09" })).EnsureSuccessStatusCode();
        var requests = app.Garmin.DataRequests.Count;
        var health = await client.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health");
        Assert.Equal(321, health!.Days.Single().Metrics.Single(x => x.Id == "calories-active").Value);
        Assert.DoesNotContain(health.Days.SelectMany(x => x.Metrics), x => x.Id == "sleep-hours");
        Assert.Single((await client.GetFromJsonAsync<RawDocumentDto[]>("/api/v1/profiles/personal/health/documents/daily-summary"))!);
        var raw = await client.GetStringAsync("/api/v1/profiles/personal/health/documents/daily-summary-envelope");
        Assert.Contains("futureEnvelope", raw); Assert.Contains("futureMetric", raw);
        Assert.Equal(requests, app.Garmin.DataRequests.Count);
    }

    [Fact]
    public async Task Activity_sync_imports_performed_strength_and_original_GPS_but_not_workout_targets()
    {
        using var app = new LocalAppFactory(); app.Garmin.IsConfigured = true; app.Garmin.Original = Encoding.UTF8.GetBytes(Gpx);
        app.Garmin.Responses["/activitylist-service/activities/search/activities?start=0&limit=50&startDate=2026-11-09&endDate=2026-11-09&excludeChildren=true"] = Json("[{\"activityId\":123,\"activityName\":\"Synthetic strength\",\"activityType\":{\"typeKey\":\"strength_training\"},\"startTimeGMT\":\"2026-11-09 08:00:00\",\"duration\":1800,\"summarizedExerciseSets\":[{\"category\":\"BENCH_PRESS\",\"reps\":40,\"sets\":4,\"maxWeight\":25000,\"volume\":1000000}]}]");
        app.Garmin.Responses["/activity-service/activity/123/workouts"] = Json("[{\"steps\":[{\"durationType\":\"REPS\",\"durationValue\":99999}]}]");
        using var client = app.CreateLocalClient();
        (await client.PutAsJsonAsync("/api/v1/connections/garmin/preferences", new { datasets = new[] { "activities" }, intervalHours = 0, lookbackDays = 1, importOriginals = true })).EnsureSuccessStatusCode();
        for (var i = 0; i < 2; i++) (await client.PostAsJsonAsync("/api/v1/connections/garmin/sync", new { startDate = "2026-11-09", endDate = "2026-11-09" })).EnsureSuccessStatusCode();
        var health = await client.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health");
        var activity = Assert.Single(health!.Activities);
        Assert.Equal(40, activity.Repetitions); Assert.Equal(1000, activity.VolumeKg); Assert.Equal(3, activity.TrackPoints);
        Assert.Equal(1000, (await client.GetFromJsonAsync<ChallengeDto[]>("/api/v1/profiles/personal/challenges"))!.Single(x => x.Metric == "strength-volume").Value);
        Assert.Equal(0, (await client.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
        Assert.Equal(activity.Id, await Import(client)); // Same decoded original, even across Garmin and file upload.
    }

    [Fact]
    public async Task Sync_defaults_are_manual_and_sensitive_datasets_are_not_selected_implicitly()
    {
        using var app = new LocalAppFactory(); using var client = app.CreateLocalClient();
        var preferences = await client.GetFromJsonAsync<SyncPreferencesDto>("/api/v1/connections/garmin/preferences");
        Assert.Equal(0, preferences!.IntervalHours);
        var datasets = await client.GetFromJsonAsync<DatasetDto[]>("/api/v1/connections/garmin/datasets");
        Assert.DoesNotContain(datasets!.Where(x => x.Sensitive), x => preferences.Datasets.Contains(x.Id));
        Assert.Empty(app.Garmin.DataRequests);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/connections/garmin/preferences", new { datasets = new[] { "unknown" }, intervalHours = 1, lookbackDays = 365, importOriginals = true })).StatusCode);
    }

    [Fact]
    public async Task Sharing_requires_accepted_friendship_and_removal_immediately_revokes_reads()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        using var friend = await app.CreateMemberAsync(owner, "accepted-friend");
        using var stranger = await app.CreateMemberAsync(owner, "not-a-friend");
        var activity = await Import(owner);
        var post = await owner.PostAsJsonAsync("/api/v1/community/posts", new { kind = "activity", sourceId = activity.ToString(), includeTrack = false }); post.EnsureSuccessStatusCode();
        Assert.Empty((await friend.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Posts);
        (await owner.PostAsJsonAsync("/api/v1/community/friends", new { username = "accepted-friend" })).EnsureSuccessStatusCode();
        var incoming = Assert.Single((await friend.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Friends);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/v1/community/friends/{incoming.Id}/accept", new { })).StatusCode);
        (await friend.PostAsJsonAsync($"/api/v1/community/friends/{incoming.Id}/accept", new { })).EnsureSuccessStatusCode();
        Assert.Single((await friend.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Posts);
        Assert.Empty((await stranger.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Posts);
        (await owner.DeleteAsync($"/api/v1/community/friends/{incoming.Id}")).EnsureSuccessStatusCode();
        Assert.Empty((await friend.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Posts);
    }

    [Fact]
    public async Task Share_preview_rejects_foreign_activity_and_crops_GPS_on_the_server()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        using var member = await app.CreateMemberAsync(owner, "share-member");
        var id = await Import(owner);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync("/api/v1/community/preview", new { kind = "activity", sourceId = id.ToString() })).StatusCode);
        var preview = await (await owner.PostAsJsonAsync("/api/v1/community/preview", new { kind = "activity", sourceId = id.ToString(), includeTrack = true })).Content.ReadFromJsonAsync<PostDto>();
        Assert.Single(preview!.Track); Assert.Null(preview.Track[0].AtUtc);
        Assert.Empty((await owner.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Posts);
    }

    [Fact]
    public async Task Uncompleted_strength_targets_cannot_be_published_as_achievements()
    {
        using var app = new LocalAppFactory(); using var client = app.CreateLocalClient();
        var response = await client.PostAsJsonAsync("/api/v1/community/preview", new { kind = "challenge", sourceId = "truck" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<CommunityDto>("/api/v1/community"))!.Posts);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var profile = await db.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
            db.Activities.Add(new TrailQuest.Api.Domain.Entities.ActivityRecord { ProfileId = profile.Id, SourceId = "synthetic-completed-target", Source = "file",
                Name = "Synthetic recorded strength", Sport = "strength_training", Date = new DateOnly(2026,11,9), StartedAtUtc = app.Clock.Now,
                DurationSeconds = 1800, VolumeKg = 10000, UpdatedAtUtc = app.Clock.Now });
            await db.SaveChangesAsync();
        }
        var earned = await client.PostAsJsonAsync("/api/v1/community/preview", new { kind = "challenge", sourceId = "truck" }); earned.EnsureSuccessStatusCode();
        var preview = (await earned.Content.ReadFromJsonAsync<PostDto>())!;
        Assert.Contains("no significa levantar un camión", preview.Summary);
        using var member = await app.CreateMemberAsync(client, "challenge-member");
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync("/api/v1/community/preview", new { kind = "challenge", sourceId = "truck" })).StatusCode);
    }

    [Fact]
    public async Task Integration_grants_enforce_subject_scope_expiry_and_revocation_and_store_only_hashes()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        using var member = await app.CreateMemberAsync(owner, "grant-member");
        (await owner.PutAsJsonAsync("/api/v1/profiles/personal/days/2026-11-09", new { steps = 1234 })).EnsureSuccessStatusCode();
        var created = await (await owner.PostAsJsonAsync("/api/v1/integrations/grants", new { application = "Synthetic client", scopes = new[] { "steps:read" }, days = 1 })).Content.ReadFromJsonAsync<CreatedGrantDto>();
        using var provider = app.CreateAnonymousClient(); provider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Token);
        Assert.DoesNotContain(created.Token, created.ToString());
        var steps = await provider.GetStringAsync("/api/v1/provider/steps?startDate=2026-11-09&endDate=2026-11-09");
        Assert.Contains("1234", steps);
        Assert.Equal(HttpStatusCode.Forbidden, (await provider.GetAsync("/api/v1/provider/activities?startDate=2026-11-09&endDate=2026-11-09")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/v1/provider/steps?startDate=2026-11-09&endDate=2026-11-09")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await provider.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.DeleteAsync($"/api/v1/integrations/grants/{created.Grant.Id}")).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>().IntegrationGrants.SingleAsync();
            Assert.NotEqual(created.Token, row.TokenHash); Assert.Equal(64, row.TokenHash.Length);
        }
        (await owner.DeleteAsync($"/api/v1/integrations/grants/{created.Grant.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await provider.GetAsync("/api/v1/provider/steps?startDate=2026-11-09&endDate=2026-11-09")).StatusCode);
    }

    [Fact]
    public async Task An_in_flight_broad_import_cannot_write_after_credential_replacement()
    {
        using var app = new LocalAppFactory(); app.Garmin.IsConfigured = true;
        var fetched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Garmin.BeforeDataFetch = async () => { fetched.TrySetResult(); await release.Task; };
        app.Garmin.Responses["/usersummary-service/v2/dailysummaries?startDate=2026-11-09&endDate=2026-11-09"] = Json("{\"data\":[{\"calendarDate\":\"2026-11-09\",\"calories\":{\"burnedActive\":321}}]}");
        using var client = app.CreateLocalClient();
        (await client.PutAsJsonAsync("/api/v1/connections/garmin/preferences", new { datasets = new[] { "daily-summary" }, intervalHours = 0, lookbackDays = 1, importOriginals = false })).EnsureSuccessStatusCode();
        var running = client.PostAsJsonAsync("/api/v1/connections/garmin/sync", new { startDate = "2026-11-09", endDate = "2026-11-09" });
        try
        {
            await fetched.Task.WaitAsync(TimeSpan.FromSeconds(10));
            (await client.PutAsJsonAsync("/api/v1/connections/garmin", new { apiKey = "fixture-replacement-key" })).EnsureSuccessStatusCode();
        }
        finally { release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.Conflict, (await running).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health"))!.Days);
    }

    [Fact]
    public async Task Expired_application_credentials_are_rejected_independently_of_browser_sessions()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        var response = await owner.PostAsJsonAsync("/api/v1/integrations/grants", new { application = "Expiring fixture", scopes = new[] { "steps:read" }, days = 1 }); response.EnsureSuccessStatusCode();
        var created = (await response.Content.ReadFromJsonAsync<CreatedGrantDto>())!;
        using var provider = app.CreateAnonymousClient(); provider.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Token);
        Assert.Equal(HttpStatusCode.OK, (await provider.GetAsync("/api/v1/provider/steps?startDate=2026-11-09&endDate=2026-11-09")).StatusCode);
        app.Clock.Now = app.Clock.Now.AddDays(2);
        Assert.Equal(HttpStatusCode.Unauthorized, (await provider.GetAsync("/api/v1/provider/steps?startDate=2026-11-09&endDate=2026-11-09")).StatusCode);
    }

    [Fact]
    public void Missing_metric_samples_stay_missing_and_hostile_XML_is_not_processed()
    {
        var document = new TrailQuest.Api.Domain.Entities.HealthDocument { Dataset = "heart-rate", Date = new DateOnly(2026,11,9), PayloadJson = "{\"heartRateValues\":[[1794200000000,null],[1794200001000,-1],[1794200002000,62]]}" };
        Assert.Single(HealthNormalizer.Series(document).Single().Samples);
        Assert.Empty(HealthNormalizer.Metrics(document));
        Assert.Throws<TrailQuest.Api.Application.AppException>(() => ActivityFileReader.Read(Encoding.UTF8.GetBytes("<!DOCTYPE gpx [<!ENTITY x SYSTEM 'file:///never-read'>]><gpx>&x;</gpx>")));
    }
}
