using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailQuest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAdventureAndHealthArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentVersion",
                table: "journeys",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DefinitionJson",
                table: "journeys",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Sport = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    DistanceMeters = table.Column<double>(type: "REAL", nullable: true),
                    CaloriesKcal = table.Column<double>(type: "REAL", nullable: true),
                    AverageHeartRate = table.Column<double>(type: "REAL", nullable: true),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: true),
                    Sets = table.Column<int>(type: "INTEGER", nullable: true),
                    VolumeKg = table.Column<double>(type: "REAL", nullable: true),
                    ExercisesJson = table.Column<string>(type: "TEXT", nullable: false),
                    TrackJson = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalFile = table.Column<byte[]>(type: "BLOB", nullable: true),
                    OriginalFormat = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalHash = table.Column<string>(type: "TEXT", nullable: true),
                    OriginalStatus = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_activities_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RequesterId = table.Column<string>(type: "TEXT", nullable: false),
                    RecipientId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_friendships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_friendships_users_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_friendships_users_RequesterId",
                        column: x => x.RequesterId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "garmin_sync_settings",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DatasetIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    LookbackDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportOriginals = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastCompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastResultJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garmin_sync_settings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_garmin_sync_settings_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "health_documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    Dataset = table.Column<string>(type: "TEXT", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_health_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_health_documents_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "integration_grants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Application = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    ScopesJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_integration_grants_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shared_posts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    TrackJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shared_posts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_blocks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    BlockedUserId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_blocks", x => new { x.UserId, x.BlockedUserId });
                    table.ForeignKey(
                        name: "FK_user_blocks_users_BlockedUserId",
                        column: x => x.BlockedUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_blocks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activities_ProfileId_OriginalHash",
                table: "activities",
                columns: new[] { "ProfileId", "OriginalHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_activities_ProfileId_Source_SourceId",
                table: "activities",
                columns: new[] { "ProfileId", "Source", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_friendships_RecipientId",
                table: "friendships",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_friendships_RequesterId_RecipientId",
                table: "friendships",
                columns: new[] { "RequesterId", "RecipientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_health_documents_ProfileId_Dataset_SourceKey",
                table: "health_documents",
                columns: new[] { "ProfileId", "Dataset", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_grants_TokenHash",
                table: "integration_grants",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_grants_UserId",
                table: "integration_grants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_shared_posts_UserId",
                table: "shared_posts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_blocks_BlockedUserId",
                table: "user_blocks",
                column: "BlockedUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "friendships");

            migrationBuilder.DropTable(
                name: "garmin_sync_settings");

            migrationBuilder.DropTable(
                name: "health_documents");

            migrationBuilder.DropTable(
                name: "integration_grants");

            migrationBuilder.DropTable(
                name: "shared_posts");

            migrationBuilder.DropTable(
                name: "user_blocks");

            migrationBuilder.DropColumn(
                name: "ContentVersion",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "DefinitionJson",
                table: "journeys");
        }
    }
}
