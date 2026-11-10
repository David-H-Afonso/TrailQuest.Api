using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrailQuest.Api.Application;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Operations;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class OperationsTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("NewPassword")]
    [InlineData("PasswordFile")]
    public void Maintenance_rejects_secret_settings_in_arguments_or_environment(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Account:Action"] = "create-owner", ["Account:Username"] = "fixture-owner", ["Account:" + key] = "synthetic rejected input"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => AccountCommand.Parse(configuration));
        Assert.DoesNotContain("synthetic rejected input", error.Message);
    }

    [Fact]
    public async Task Redirected_secret_input_is_bounded_and_not_trimmed()
    {
        Assert.Equal("  synthetic phrase  ", await AccountCommand.ReadSecretLineAsync(new StringReader("  synthetic phrase  \nignored")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AccountCommand.ReadSecretLineAsync(new StringReader(new string('x', 129))));
    }

    [Fact]
    public void A_second_process_cannot_share_a_live_application_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", "trailquest-lock-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (ApplicationInstance.Acquire(directory)) Assert.Throws<InvalidOperationException>(() => ApplicationInstance.Acquire(directory));
            using var next = ApplicationInstance.Acquire(directory);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Owner_recovery_revokes_sessions_and_keeps_profiles_and_progress()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        (await owner.PutAsJsonAsync("/api/v1/profiles/personal/days/2026-11-09", new { steps = 12345 })).EnsureSuccessStatusCode();
        var before = (await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!;
        using var member = await app.CreateMemberAsync(owner, "fixture-member");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AccountService>();
            var error = await Assert.ThrowsAsync<AppException>(() => service.RecoverOwnerAsync("fixture-member", "Replacement fixture passphrase 82!", default));
            Assert.Equal("owner_not_found", error.Code);
            await service.RecoverOwnerAsync("fixture-owner", "Replacement fixture passphrase 82!", default);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
        (await owner.PostAsJsonAsync("/api/v1/auth/login", new { username = "fixture-owner", password = "Replacement fixture passphrase 82!" })).EnsureSuccessStatusCode();
        var after = (await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!;
        Assert.NotNull(before.CurrentJourney); Assert.NotNull(after.CurrentJourney);
        Assert.Equal(before.History, after.History); Assert.Equal(before.CurrentJourney.Visits, after.CurrentJourney.Visits);
        Assert.Equal(before.CurrentJourney.StartedOn, after.CurrentJourney.StartedOn);
        Assert.False((await member.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User!.IsOwner);
    }

    [Fact]
    public async Task Summary_and_strength_queries_do_not_materialize_originals_or_track_json()
    {
        using var app = new LocalAppFactory(demoEnabled: false); using var owner = app.CreateLocalClient();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var profile = await db.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
            db.Activities.Add(new() { ProfileId = profile.Id, SourceId = "synthetic", Name = "Synthetic session", Sport = "strength_training",
                Date = new(2026, 11, 9), StartedAtUtc = app.Clock.Now, TrackJson = "not materialized by summary", TrackPointCount = 9, OriginalFile = [1,2,3], VolumeKg = 1200 });
            await db.SaveChangesAsync();
        }
        var overview = (await owner.GetFromJsonAsync<HealthOverviewDto>("/api/v1/profiles/personal/health"))!;
        Assert.Equal(9, Assert.Single(overview.Activities).TrackPoints);
        Assert.NotEmpty((await owner.GetFromJsonAsync<ChallengeDto[]>("/api/v1/profiles/personal/challenges"))!);
    }

    [Fact]
    public async Task Export_is_bounded_can_be_partitioned_and_does_not_remove_records()
    {
        using var app = new LocalAppFactory(demoEnabled: false); using var owner = app.CreateLocalClient();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrailQuestDbContext>();
            var profile = await db.Profiles.SingleAsync(x => x.Kind == "personal" && x.UserId != null);
            db.HealthDocuments.Add(new() { ProfileId = profile.Id, Dataset = "synthetic", SourceKey = "large", Date = new(2026, 1, 1), PayloadJson = "{\"data\":\"" + new string('x', ArchiveReadBudget.MaxJsonCharacters) + "\"}" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await owner.GetAsync("/api/v1/profiles/personal/export")).StatusCode);
        var response = await owner.GetAsync("/api/v1/profiles/personal/export?startDate=2026-11-01&endDate=2026-11-09"); response.EnsureSuccessStatusCode();
        Assert.Equal("2026-11-01/2026-11-09", response.Headers.GetValues("X-TrailQuest-Export-Range").Single());
        Assert.Empty((await response.Content.ReadFromJsonAsync<ExportDto>())!.HealthDocuments);
        await using var check = app.Services.CreateAsyncScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<TrailQuestDbContext>().HealthDocuments.CountAsync());
    }

    [Fact]
    public async Task Export_concurrency_is_scoped_to_the_authenticated_account()
    {
        using var app = new LocalAppFactory(); using var owner = app.CreateLocalClient();
        var user = (await owner.GetFromJsonAsync<SessionDto>("/api/v1/auth/session"))!.User!;
        using var lease = app.Services.GetRequiredService<ExportCoordinator>().Enter(user.Id);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await owner.GetAsync("/api/v1/profiles/personal/export")).StatusCode);
    }

    [Fact]
    public async Task Internal_status_routes_do_not_open_access_to_private_data()
    {
        using var app = new LocalAppFactory(); using var anonymous = app.CreateAnonymousClient();
        (await anonymous.GetAsync("/health/live")).EnsureSuccessStatusCode();
        (await anonymous.GetAsync("/health/ready")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/unknown-endpoint")).StatusCode);
        Assert.Equal("1.0.0", (await anonymous.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/health")).GetProperty("version").GetString());
    }
}
