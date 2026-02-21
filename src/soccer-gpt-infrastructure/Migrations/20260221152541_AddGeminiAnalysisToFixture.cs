using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soccer_gpt_infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGeminiAnalysisToFixture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fixtures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiId = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeTeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayTeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    HomeGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeGoalAvg = table.Column<double>(type: "REAL", nullable: false),
                    AwayGoalAvg = table.Column<double>(type: "REAL", nullable: false),
                    HtHomeGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    HtAwayGoal = table.Column<int>(type: "INTEGER", nullable: false),
                    HtHomeGoalAvg = table.Column<double>(type: "REAL", nullable: false),
                    HtAwayGoalAvg = table.Column<double>(type: "REAL", nullable: false),
                    HomeShots = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayShots = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeShotsOnTarget = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayShotsOnTarget = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeBallPossession = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayBallPossession = table.Column<int>(type: "INTEGER", nullable: true),
                    HomePassesAccurate = table.Column<int>(type: "INTEGER", nullable: true),
                    AwayPassesAccurate = table.Column<int>(type: "INTEGER", nullable: true),
                    HomeXg = table.Column<double>(type: "REAL", nullable: false),
                    AwayXg = table.Column<double>(type: "REAL", nullable: false),
                    HomeWinOdds = table.Column<double>(type: "REAL", nullable: true),
                    DrawOdds = table.Column<double>(type: "REAL", nullable: true),
                    AwayWinOdds = table.Column<double>(type: "REAL", nullable: true),
                    Over25Odds = table.Column<double>(type: "REAL", nullable: true),
                    Under25Odds = table.Column<double>(type: "REAL", nullable: true),
                    BttsYesOdds = table.Column<double>(type: "REAL", nullable: true),
                    IsCurrentSeason = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDerby = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GeminiRecommendation = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiConfidence = table.Column<double>(type: "REAL", nullable: true),
                    GeminiReasoning = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiAnalysis = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiIsTrap = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApiId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalsFor = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalsAgainst = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalsDiff = table.Column<int>(type: "INTEGER", nullable: false),
                    Played = table.Column<int>(type: "INTEGER", nullable: false),
                    Win = table.Column<int>(type: "INTEGER", nullable: false),
                    Lose = table.Column<int>(type: "INTEGER", nullable: false),
                    Draw = table.Column<int>(type: "INTEGER", nullable: false),
                    Form = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_ApiId",
                table: "Fixtures",
                column: "ApiId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ApiId",
                table: "Teams",
                column: "ApiId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fixtures");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
