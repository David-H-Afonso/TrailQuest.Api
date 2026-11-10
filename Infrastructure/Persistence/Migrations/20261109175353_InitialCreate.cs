using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailQuest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DailyStepGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    StepLengthCm = table.Column<double>(type: "REAL", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SelectedRouteId = table.Column<string>(type: "TEXT", nullable: false),
                    LastGarminSyncAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "achievement_unlocks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    AchievementId = table.Column<string>(type: "TEXT", nullable: false),
                    UnlockedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_achievement_unlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_achievement_unlocks_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "daily_steps",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Steps = table.Column<int>(type: "INTEGER", nullable: false),
                    DistanceMeters = table.Column<double>(type: "REAL", nullable: false),
                    StepLengthCm = table.Column<double>(type: "REAL", nullable: false),
                    IsDistanceEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_daily_steps_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "journeys",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    RouteId = table.Column<string>(type: "TEXT", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_journeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_journeys_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "checkpoint_visits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JourneyId = table.Column<long>(type: "INTEGER", nullable: false),
                    CheckpointId = table.Column<string>(type: "TEXT", nullable: false),
                    UnlockedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_checkpoint_visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_checkpoint_visits_journeys_JourneyId",
                        column: x => x.JourneyId,
                        principalTable: "journeys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_achievement_unlocks_ProfileId_AchievementId",
                table: "achievement_unlocks",
                columns: new[] { "ProfileId", "AchievementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_checkpoint_visits_JourneyId_CheckpointId",
                table: "checkpoint_visits",
                columns: new[] { "JourneyId", "CheckpointId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_steps_ProfileId_Date",
                table: "daily_steps",
                columns: new[] { "ProfileId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journeys_ProfileId_RouteId",
                table: "journeys",
                columns: new[] { "ProfileId", "RouteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "achievement_unlocks");

            migrationBuilder.DropTable(
                name: "checkpoint_visits");

            migrationBuilder.DropTable(
                name: "daily_steps");

            migrationBuilder.DropTable(
                name: "journeys");

            migrationBuilder.DropTable(
                name: "profiles");
        }
    }
}
