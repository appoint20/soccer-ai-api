using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <inheritdoc />
    public partial class AddHeadToHeadMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "HeadToHeadCheckedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HeadToHeadMeetings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiFixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeTeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayTeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<long>(type: "INTEGER", nullable: false),
                    HomeGoals = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayGoals = table.Column<int>(type: "INTEGER", nullable: false),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: true),
                    CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
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
