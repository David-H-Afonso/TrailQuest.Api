using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TrailQuest.Api.Contracts;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class LegacyMigrationTests
{
    [Fact]
    public async Task Original_activity_is_unchanged_and_can_only_be_claimed_by_the_human_created_owner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", $"trailquest-legacy-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<TrailQuestDbContext>().UseSqlite($"Data Source={Path.Combine(directory, "trailquest.db")};Pooling=False").Options;
            await using (var original = new TrailQuestDbContext(options))
            {
                await original.GetService<IMigrator>().MigrateAsync("20261109175353_InitialCreate");
                await original.Database.ExecuteSqlRawAsync("INSERT INTO profiles (Id, DailyStepGoal, StepLengthCm, TimeZoneId, SelectedRouteId) VALUES ('personal', 8000, 72, 'UTC', 'tokyo');");
                await original.Database.ExecuteSqlRawAsync("INSERT INTO daily_steps (ProfileId, Date, Steps, DistanceMeters, StepLengthCm, IsDistanceEstimated, Source, UpdatedAtUtc) VALUES ('personal', '2026-11-09', 2468, 1789, 72, 0, 'garmin', '2026-11-09T12:00:00+00:00');");
                await original.Database.ExecuteSqlRawAsync("INSERT INTO journeys (ProfileId, RouteId, StartedOn, CreatedAtUtc) VALUES ('personal', 'tokyo', '2026-11-09', '2026-11-09T12:00:00+00:00');");
            }
            using var app = new LocalAppFactory(directory);
            using (var anonymous = app.CreateAnonymousClient())
            {
                var session = await anonymous.GetFromJsonAsync<SessionDto>("/api/v1/auth/session");
                Assert.True(session!.SetupRequired);
                Assert.Null(session.User);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/profiles/personal/dashboard")).StatusCode);
            }
            using var owner = app.CreateLocalClient();
            var preserved = await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard");
            Assert.Equal(2468, preserved!.Totals.Steps);
            Assert.Equal(1.789, preserved.Totals.DistanceKm, 4);
            Assert.Equal("garmin", Assert.Single(preserved.History).Source);
            Assert.False(preserved.TodayActivity.IsDistanceEstimated);
            using var member = await app.CreateMemberAsync(owner, "legacy-member");
            Assert.Equal(0, (await member.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
            Assert.Equal(2468, (await owner.GetFromJsonAsync<DashboardDto>("/api/v1/profiles/personal/dashboard"))!.Totals.Steps);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
