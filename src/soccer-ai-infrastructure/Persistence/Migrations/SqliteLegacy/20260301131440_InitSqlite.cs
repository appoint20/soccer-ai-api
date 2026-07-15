using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FixtureAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FixtureApiId = table.Column<int>(type: "INTEGER", nullable: false),
                    Lang = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Recommendation = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    PredictionReason = table.Column<string>(type: "TEXT", nullable: false),
                    Analysis = table.Column<string>(type: "TEXT", nullable: false),
                    TrapDetected = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrapReason = table.Column<string>(type: "TEXT", nullable: true),
                    ConsensusEvaluation = table.Column<string>(type: "TEXT", nullable: false),
                    BttsSummary = table.Column<string>(type: "TEXT", nullable: true),
                    Over25Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Under25Summary = table.Column<string>(type: "TEXT", nullable: true),
                    HomeWinSummary = table.Column<string>(type: "TEXT", nullable: true),
                    AwayWinSummary = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixtureAnalyses", x => x.Id);
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
                    Elo = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.UniqueConstraint("AK_Teams_ApiId", x => x.ApiId);
                });

            migrationBuilder.CreateTable(
                name: "UserCombinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalOdds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCombinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

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
                    Date = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
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
                    HomeElo = table.Column<double>(type: "REAL", nullable: true),
                    AwayElo = table.Column<double>(type: "REAL", nullable: true),
                    VenueSurface = table.Column<string>(type: "TEXT", nullable: true),
                    VenueCity = table.Column<string>(type: "TEXT", nullable: true),
                    Temp = table.Column<double>(type: "REAL", nullable: true),
                    Humidity = table.Column<int>(type: "INTEGER", nullable: true),
                    WeatherDesc = table.Column<string>(type: "TEXT", nullable: true),
                    IsCurrentSeason = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDerby = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    GeminiRecommendation = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiConfidence = table.Column<double>(type: "REAL", nullable: true),
                    GeminiReasoning = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiAnalysis = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiIsTrap = table.Column<bool>(type: "INTEGER", nullable: true),
                    GeminiTrapReason = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiOneLineSummary = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiBttsSummary = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiOver25Summary = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiUnder25Summary = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiHomeWinSummary = table.Column<string>(type: "TEXT", nullable: true),
                    GeminiAwayWinSummary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fixtures_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "ApiId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fixtures_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "ApiId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserCombinationMatch",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserCombinationId = table.Column<int>(type: "INTEGER", nullable: false),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Market = table.Column<string>(type: "TEXT", nullable: false),
                    Prediction = table.Column<string>(type: "TEXT", nullable: false),
                    Odds = table.Column<double>(type: "REAL", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCombinationMatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCombinationMatch_UserCombinations_UserCombinationId",
                        column: x => x.UserCombinationId,
                        principalTable: "UserCombinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixtureAnalyses_FixtureApiId_Lang",
                table: "FixtureAnalyses",
                columns: new[] { "FixtureApiId", "Lang" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_ApiId",
                table: "Fixtures",
                column: "ApiId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_AwayTeamId",
                table: "Fixtures",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_HomeTeamId",
                table: "Fixtures",
                column: "HomeTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ApiId",
                table: "Teams",
                column: "ApiId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCombinationMatch_UserCombinationId",
                table: "UserCombinationMatch",
                column: "UserCombinationId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixtureAnalyses");

            migrationBuilder.DropTable(
                name: "Fixtures");

            migrationBuilder.DropTable(
                name: "UserCombinationMatch");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "UserCombinations");
        }
    }
}
