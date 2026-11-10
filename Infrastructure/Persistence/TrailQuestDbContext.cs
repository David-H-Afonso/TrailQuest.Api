using Microsoft.EntityFrameworkCore;
using TrailQuest.Api.Domain.Entities;

namespace TrailQuest.Api.Infrastructure.Persistence;

public sealed class TrailQuestDbContext(DbContextOptions<TrailQuestDbContext> options) : DbContext(options)
{
    public DbSet<ProfileSettings> Profiles => Set<ProfileSettings>();
    public DbSet<DailySteps> DailySteps => Set<DailySteps>();
    public DbSet<Journey> Journeys => Set<Journey>();
    public DbSet<CheckpointVisit> CheckpointVisits => Set<CheckpointVisit>();
    public DbSet<AchievementUnlock> AchievementUnlocks => Set<AchievementUnlock>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<GarminConnection> GarminConnections => Set<GarminConnection>();
    public DbSet<ActivityRecord> Activities => Set<ActivityRecord>();
    public DbSet<HealthDocument> HealthDocuments => Set<HealthDocument>();
    public DbSet<GarminSyncSettings> SyncSettings => Set<GarminSyncSettings>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> Blocks => Set<UserBlock>();
    public DbSet<SharedPost> Posts => Set<SharedPost>();
    public DbSet<IntegrationGrant> IntegrationGrants => Set<IntegrationGrant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActivityRecord>(entity =>
        {
            entity.ToTable("activities");
            entity.HasIndex(x => new { x.ProfileId, x.Source, x.SourceId }).IsUnique();
            entity.HasIndex(x => new { x.ProfileId, x.OriginalHash }).IsUnique();
            entity.HasOne<ProfileSettings>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<HealthDocument>(entity =>
        {
            entity.ToTable("health_documents");
            entity.HasIndex(x => new { x.ProfileId, x.Dataset, x.SourceKey }).IsUnique();
            entity.HasOne<ProfileSettings>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GarminSyncSettings>(entity =>
        {
            entity.ToTable("garmin_sync_settings");
            entity.HasKey(x => x.UserId);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.ToTable("friendships");
            entity.HasIndex(x => new { x.RequesterId, x.RecipientId }).IsUnique();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RecipientId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserBlock>(entity =>
        {
            entity.ToTable("user_blocks");
            entity.HasKey(x => new { x.UserId, x.BlockedUserId });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.BlockedUserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SharedPost>(entity =>
        {
            entity.ToTable("shared_posts");
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IntegrationGrant>(entity =>
        {
            entity.ToTable("integration_grants");
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(32);
            entity.Property(x => x.NormalizedUsername).HasMaxLength(32);
            entity.Property(x => x.DisplayName).HasMaxLength(60);
        });
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(x => x.IdHash);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<GarminConnection>(entity =>
        {
            entity.ToTable("garmin_connections");
            entity.HasKey(x => new { x.UserId, x.InstallationId });
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProfileSettings>(entity =>
        {
            entity.ToTable("profiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(64);
            entity.Property(x => x.Kind).HasMaxLength(20);
            entity.HasIndex(x => new { x.UserId, x.Kind }).IsUnique();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.TimeZoneId).HasMaxLength(100);
        });
        modelBuilder.Entity<DailySteps>(entity =>
        {
            entity.ToTable("daily_steps");
            entity.HasIndex(x => new { x.ProfileId, x.Date }).IsUnique();
            entity.HasOne<ProfileSettings>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Journey>(entity =>
        {
            entity.ToTable("journeys");
            entity.HasIndex(x => new { x.ProfileId, x.RouteId }).IsUnique();
            entity.HasIndex(x => x.ProfileId).IsUnique().HasDatabaseName("IX_journeys_ProfileId_Active")
                .HasFilter("\"LeftAtUtc\" IS NULL");
            entity.HasOne<ProfileSettings>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CheckpointVisit>(entity =>
        {
            entity.ToTable("checkpoint_visits");
            entity.HasIndex(x => new { x.JourneyId, x.CheckpointId }).IsUnique();
            entity.HasOne<Journey>().WithMany().HasForeignKey(x => x.JourneyId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<AchievementUnlock>(entity =>
        {
            entity.ToTable("achievement_unlocks");
            entity.HasIndex(x => new { x.ProfileId, x.AchievementId }).IsUnique();
            entity.HasOne<ProfileSettings>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
