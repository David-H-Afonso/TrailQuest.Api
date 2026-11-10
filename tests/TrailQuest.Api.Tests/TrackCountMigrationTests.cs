using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TrailQuest.Api.Domain.Entities;
using TrailQuest.Api.Infrastructure.Persistence;

namespace TrailQuest.Api.Tests;

public sealed class TrackCountMigrationTests
{
    [Fact]
    public async Task Cached_count_upgrade_preserves_existing_track_and_original_bytes()
    {
        var options = new DbContextOptionsBuilder<TrailQuestDbContext>().UseSqlite("Data Source=:memory:").Options;
        await using var db = new TrailQuestDbContext(options);
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261110101001_ExpandAdventureAndHealthArchive");
        var profile = new ProfileSettings { Id = "synthetic-migration", Kind = "personal" };
        db.Profiles.Add(profile); await db.SaveChangesAsync();
        const string track = "[{\"latitude\":35,\"longitude\":139},{\"latitude\":35.01,\"longitude\":139.01}]";
        byte[] original = [10, 20, 30, 40];
        var instant = DateTimeOffset.Parse("2026-11-09T10:00:00Z");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO activities (ProfileId,SourceId,Source,Name,Sport,StartedAtUtc,Date,TimeZoneId,ExercisesJson,TrackJson,OriginalFile,OriginalFormat,OriginalHash,OriginalStatus,UpdatedAtUtc) VALUES ({profile.Id},{"legacy-fixture"},{"file"},{"Synthetic recording"},{"walking"},{instant},{new DateOnly(2026,11,9)},{"UTC"},{"[]"},{track},{original},{"gpx"},{"synthetic-hash"},{"imported"},{instant})");
        await migrator.MigrateAsync();
        var row = await db.Activities.AsNoTracking().SingleAsync();
        Assert.Equal(2, row.TrackPointCount);
        Assert.Equal(track, row.TrackJson); Assert.Equal(original, row.OriginalFile);
        Assert.Equal(instant, row.StartedAtUtc); Assert.Equal("synthetic-hash", row.OriginalHash);
        Assert.Equal(profile.Id, row.ProfileId);
    }
}
