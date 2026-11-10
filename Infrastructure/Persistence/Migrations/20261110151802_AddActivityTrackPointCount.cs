using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrailQuest.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityTrackPointCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrackPointCount",
                table: "activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
            // Count existing points without replacing the recording, original file or rewards.
            migrationBuilder.Sql("UPDATE activities SET TrackPointCount = CASE WHEN json_valid(TrackJson) THEN json_array_length(TrackJson) ELSE 0 END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrackPointCount",
                table: "activities");
        }
    }
}
