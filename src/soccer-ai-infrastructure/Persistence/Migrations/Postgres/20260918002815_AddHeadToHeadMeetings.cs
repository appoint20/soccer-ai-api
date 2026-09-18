using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddHeadToHeadMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HeadToHeadCheckedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HeadToHeadMeetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiFixtureId = table.Column<int>(type: "integer", nullable: false),
                    HomeTeamId = table.Column<int>(type: "integer", nullable: false),
                    AwayTeamId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HomeGoals = table.Column<int>(type: "integer", nullable: false),
                    AwayGoals = table.Column<int>(type: "integer", nullable: false),
                    LeagueId = table.Column<int>(type: "integer", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeadToHeadMeetings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HeadToHeadMeetings_ApiFixtureId",
                table: "HeadToHeadMeetings",
                column: "ApiFixtureId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeadToHeadMeetings_AwayTeamId_HomeTeamId_Date",
                table: "HeadToHeadMeetings",
                columns: new[] { "AwayTeamId", "HomeTeamId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_HeadToHeadMeetings_HomeTeamId_AwayTeamId_Date",
                table: "HeadToHeadMeetings",
                columns: new[] { "HomeTeamId", "AwayTeamId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HeadToHeadMeetings");

            migrationBuilder.DropColumn(
                name: "HeadToHeadCheckedAtUtc",
                table: "Fixtures");
        }
    }
}
