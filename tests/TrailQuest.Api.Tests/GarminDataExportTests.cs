using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class GarminDataExportTests
{
    private static readonly DateOnly First = new(2026, 11, 8);
    private static readonly DateOnly Last = new(2026, 11, 9);

    private static async Task SeedAsync(LocalAppFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
        var profile = await db.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
        db.HealthDocuments.AddRange(
            new HealthDocument { ProfileId = profile.Id, Dataset = "sleep", SourceKey = "sleep-1", Date = First, PayloadJson = "{\"sleepTimeSeconds\":28800,\"sleepScores\":{\"overall\":{\"value\":88}},\"fixtureTag\":\"synthetic-sleep\"}" },
            new HealthDocument { ProfileId = profile.Id, Dataset = "sleep-envelope", SourceKey = "sleep-envelope-1", Date = First, PayloadJson = "{\"response\":{\"fixtureTag\":\"synthetic-envelope\"}}" },
            new HealthDocument { ProfileId = profile.Id, Dataset = "body-battery", SourceKey = "body-1", Date = Last, PayloadJson = "{\"bodyBatteryValuesArray\":[{\"value\":61}],\"fixtureTag\":\"synthetic-body-battery\"}" },
            new HealthDocument { ProfileId = profile.Id, Dataset = "weight", SourceKey = "weight-1", Date = First, PayloadJson = "{\"weight\":70.5,\"fixtureTag\":\"synthetic-sensitive-weight\"}" },
            new HealthDocument { ProfileId = profile.Id, Dataset = "sleep", Source = "file", SourceKey = "non-garmin", Date = First, PayloadJson = "{\"fixtureTag\":\"synthetic-non-garmin\"}" },
            new HealthDocument { ProfileId = profile.Id, Dataset = "sleep", SourceKey = "out-of-range", Date = Last.AddDays(1), PayloadJson = "{\"fixtureTag\":\"synthetic-out-of-range\"}" },
            new HealthDocument { ProfileId = profile.Id, Dataset = "activity-detail", SourceKey = "activity-detail-1", Date = Last, PayloadJson = "{\"geoPolylineDTO\":\"SYNTHETIC_GPS_SENTINEL\"}" });
        db.DailySteps.AddRange(
            new DailySteps { ProfileId = profile.Id, Date = First, Steps = 8200, DistanceMeters = 6100, StepLengthCm = 72, Source = "garmin", UpdatedAtUtc = app.Clock.Now },
            new DailySteps { ProfileId = profile.Id, Date = Last, Steps = 4300, DistanceMeters = 3100, StepLengthCm = 72, Source = "manual", UpdatedAtUtc = app.Clock.Now });
        db.Activities.AddRange(
            new ActivityRecord
            {
                ProfileId = profile.Id, Source = "garmin", SourceId = "fixture-garmin-activity", Name = "Synthetic Garmin walk",
                Sport = "walking", Date = Last, StartedAtUtc = app.Clock.Now, DurationSeconds = 1800,
                DistanceMeters = 2500, CaloriesKcal = 120, AverageHeartRate = 112,
                ExercisesJson = "[]", TrackJson = "[{\"latitude\":51.5014,\"longitude\":-0.1419}]",
                TrackPointCount = 1, OriginalFile = [0x50, 0x4b, 0x47], OriginalFormat = "fit"
            },
            new ActivityRecord
            {
                ProfileId = profile.Id, Source = "file", SourceId = "fixture-upload", Name = "Synthetic uploaded activity",
                Sport = "walking", Date = Last, StartedAtUtc = app.Clock.Now, ExercisesJson = "[]"
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Selected_days_and_datasets_return_only_matching_garmin_documents()
    {
        using var app = new LocalAppFactory(demoEnabled: false);
        using var owner = app.CreateLocalClient();
        await SeedAsync(app);
        var response = await owner.GetAsync($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate={Last:yyyy-MM-dd}&datasets=sleep&datasets=body-battery");
        response.EnsureSuccessStatusCode();
        Assert.Equal($"{First:yyyy-MM-dd}/{Last:yyyy-MM-dd}", response.Headers.GetValues("X-TrailQuest-Export-Range").Single());
        Assert.Contains("attachment;", response.Content.Headers.ContentDisposition!.ToString());
        var export = await response.Content.ReadFromJsonAsync<GarminDataExportDto>();

        Assert.NotNull(export);
        Assert.Equal(1, export.SchemaVersion);
        Assert.Equal("personal", export.ProfileId);
        Assert.Equal(First, export.StartDate);
        Assert.Equal(Last, export.EndDate);
        Assert.Equal(new[] { "sleep", "body-battery" }, export.Datasets);
        Assert.Empty(export.SensitiveDatasets);
        Assert.Equal(2, export.Days.Count);
        Assert.Empty(export.Days.SelectMany(x => x.Steps));
        Assert.Empty(export.Days.SelectMany(x => x.Activities));
        Assert.Equal(new[] { "sleep", "sleep-envelope" }, export.Days[0].HealthRecords.Select(x => x.Dataset).Order().ToArray());
        Assert.Equal("synthetic-sleep", export.Days[0].HealthRecords.Single(x => x.Dataset == "sleep").Data.GetProperty("fixtureTag").GetString());
        Assert.Equal("body-battery", Assert.Single(export.Days[1].HealthRecords).Dataset);
        Assert.DoesNotContain("synthetic-sensitive-weight", JsonSerializer.Serialize(export));
        Assert.DoesNotContain("synthetic-non-garmin", JsonSerializer.Serialize(export));
        Assert.DoesNotContain("synthetic-out-of-range", JsonSerializer.Serialize(export));
        Assert.False(export.Privacy.IncludesCredentials);
        Assert.False(export.Privacy.IncludesOriginalActivityFiles);
        Assert.False(export.Privacy.IncludesGpsTracks);
    }

    [Fact]
    public async Task All_datasets_include_sensitive_rows_only_after_explicit_all_selection_and_never_export_gps_or_original_files()
    {
        using var app = new LocalAppFactory(demoEnabled: false);
        using var owner = app.CreateLocalClient();
        await SeedAsync(app);

        var export = await owner.GetFromJsonAsync<GarminDataExportDto>($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate={Last:yyyy-MM-dd}&datasets=all");

        Assert.NotNull(export);
        Assert.Contains("weight", export.SensitiveDatasets);
        Assert.Contains("cycle-detail", export.SensitiveDatasets);
        Assert.Single(export.Days[0].Steps);
        Assert.Equal(8200, export.Days[0].Steps[0].Steps);
        Assert.Empty(export.Days[1].Steps); // A manual day is not mislabeled as Garmin data.
        Assert.Single(export.Days[1].Activities);
        Assert.Equal("Synthetic Garmin walk", export.Days[1].Activities[0].Name);
        Assert.DoesNotContain("SYNTHETIC_GPS_SENTINEL", JsonSerializer.Serialize(export));
        Assert.DoesNotContain("51.5014", JsonSerializer.Serialize(export));
        Assert.DoesNotContain("PKG", JsonSerializer.Serialize(export));
        Assert.DoesNotContain("Synthetic uploaded activity", JsonSerializer.Serialize(export));
        Assert.Contains("synthetic-sensitive-weight", JsonSerializer.Serialize(export));

        using var member = await app.CreateMemberAsync(owner, "fixture-export-member");
        var memberExport = await member.GetFromJsonAsync<GarminDataExportDto>($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate={Last:yyyy-MM-dd}&datasets=all");
        Assert.NotNull(memberExport);
        Assert.DoesNotContain("synthetic-sensitive-weight", JsonSerializer.Serialize(memberExport));
        Assert.Empty(memberExport.Days.SelectMany(x => x.HealthRecords));
    }

    [Fact]
    public async Task Garmin_export_requires_authentication_personal_profile_valid_range_and_known_datasets()
    {
        using var app = new LocalAppFactory(demoEnabled: false);
        using var anonymous = app.CreateAnonymousClient();
        using var owner = app.CreateLocalClient();
        var range = $"startDate={First:yyyy-MM-dd}&endDate={Last:yyyy-MM-dd}&datasets=sleep";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/profiles/personal/health/export?{range}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/profiles/demo/health/export?{range}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate={Last:yyyy-MM-dd}&datasets=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate=2028-01-01&datasets=sleep")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/v1/profiles/personal/health/export?startDate=2025-11-08&endDate=2026-11-09&datasets=sleep")).StatusCode);
    }

    [Fact]
    public async Task Garmin_export_shares_the_account_scoped_export_concurrency_guard()
    {
        using var app = new LocalAppFactory();
        using var owner = app.CreateLocalClient();
        var user = (await owner.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User!;
        using var lease = app.Services.GetRequiredService<ExportCoordinator>().Enter(user.Id);
        var response = await owner.GetAsync($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate={Last:yyyy-MM-dd}&datasets=sleep");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Contains("export_in_progress", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Garmin_export_reuses_the_transactional_archive_read_budget_without_truncation()
    {
        using var app = new LocalAppFactory(demoEnabled: false);
        using var owner = app.CreateLocalClient();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var profile = await db.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
            db.HealthDocuments.Add(new HealthDocument
            {
                ProfileId = profile.Id,
                Dataset = "sleep",
                SourceKey = "synthetic-too-large",
                Date = First,
                PayloadJson = "{\"data\":\"" + new string('x', ArchiveReadBudget.MaxJsonCharacters) + "\"}"
            });
            await db.SaveChangesAsync();
        }

        var response = await owner.GetAsync($"/api/v1/profiles/personal/health/export?startDate={First:yyyy-MM-dd}&endDate={First:yyyy-MM-dd}&datasets=sleep");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("archive_too_large", await response.Content.ReadAsStringAsync());
    }
}
