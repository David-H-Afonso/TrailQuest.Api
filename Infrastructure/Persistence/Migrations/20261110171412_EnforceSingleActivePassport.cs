using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailQuest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActivePassport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "CountingFrom",
                table: "journeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "InitialDayDistanceKm",
                table: "journeys",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "InitialDaySteps",
                table: "journeys",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeftAtUtc",
                table: "journeys",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SavedDistanceKm",
                table: "journeys",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SavedSteps",
                table: "journeys",
                type: "INTEGER",
                nullable: true);

            // Preserve every row/reward. Only the oldest enrollment in each isolated
            // profile remains active; startup snapshots the others in that profile's
            // IANA calendar before any new progress can be imported.
            migrationBuilder.Sql("""
                UPDATE journeys
                SET LeftAtUtc = strftime('%Y-%m-%d %H:%M:%f+00:00', 'now')
                WHERE Id <> (
                    SELECT oldest.Id FROM journeys AS oldest
                    WHERE oldest.ProfileId = journeys.ProfileId
                    ORDER BY oldest.StartedOn, oldest.CreatedAtUtc, oldest.Id
                    LIMIT 1
                );
                UPDATE profiles
                SET SelectedRouteId = COALESCE((
                    SELECT RouteId FROM journeys
                    WHERE journeys.ProfileId = profiles.Id AND LeftAtUtc IS NULL
                    LIMIT 1
                ), '');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_journeys_ProfileId_Active",
                table: "journeys",
                column: "ProfileId",
                unique: true,
                filter: "\"LeftAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journeys_ProfileId_Active",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "CountingFrom",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "InitialDayDistanceKm",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "InitialDaySteps",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "LeftAtUtc",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "SavedDistanceKm",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "SavedSteps",
                table: "journeys");
        }
    }
}
