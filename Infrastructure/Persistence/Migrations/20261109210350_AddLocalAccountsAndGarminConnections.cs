using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailQuest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalAccountsAndGarminConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "profiles",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // Preserve legacy IDs and every activity/reward row; the human-created owner claims them later.
            migrationBuilder.Sql("UPDATE profiles SET Kind = CASE WHEN Id = 'demo' THEN 'demo' ELSE 'personal' END;");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailedLogins = table.Column<int>(type: "INTEGER", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "garmin_connections",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    InstallationId = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedApiKey = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garmin_connections", x => new { x.UserId, x.InstallationId });
                    table.ForeignKey(
                        name: "FK_garmin_connections_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    IdHash = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.IdHash);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_profiles_UserId_Kind",
                table: "profiles",
                columns: new[] { "UserId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_UserId",
                table: "user_sessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedUsername",
                table: "users",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_profiles_users_UserId",
                table: "profiles",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_profiles_users_UserId",
                table: "profiles");

            migrationBuilder.DropTable(
                name: "garmin_connections");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "IX_profiles_UserId_Kind",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "profiles");
        }
    }
}
