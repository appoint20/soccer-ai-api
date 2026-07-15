using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <summary>
    /// Fresh initial schema for PostgreSQL. Attributes bind this migration to
    /// PostgresDbContext so the legacy SQLite migrations (bound to
    /// ApplicationDbContext) are never applied against Postgres and vice versa.
    /// </summary>
    [DbContext(typeof(PostgresDbContext))]
    [Migration("20260714130000_InitPostgres")]
    public partial class InitPostgres : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ShortName = table.Column<string>(type: "text", nullable: true),
                    LeagueId = table.Column<int>(type: "integer", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false),
                    GoalsFor = table.Column<int>(type: "integer", nullable: false),
                    GoalsAgainst = table.Column<int>(type: "integer", nullable: false),
                    GoalsDiff = table.Column<int>(type: "integer", nullable: false),
                    Played = table.Column<int>(type: "integer", nullable: false),
                    Win = table.Column<int>(type: "integer", nullable: false),
                    Lose = table.Column<int>(type: "integer", nullable: false),
                    Draw = table.Column<int>(type: "integer", nullable: false),
                    Form = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Elo = table.Column<double>(type: "double precision", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ManagerName = table.Column<string>(type: "text", nullable: true),
                    ManagerAppointedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.UniqueConstraint("AK_Teams_ApiId", x => x.ApiId);
                });

            migrationBuilder.CreateTable(
                name: "Fixtures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiId = table.Column<int>(type: "integer", nullable: false),
                    HomeTeamId = table.Column<int>(type: "integer", nullable: false),
                    AwayTeamId = table.Column<int>(type: "integer", nullable: false),
                    LeagueId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    HomeGoal = table.Column<int>(type: "integer", nullable: false),
                    AwayGoal = table.Column<int>(type: "integer", nullable: false),
                    HomeGoalAvg = table.Column<double>(type: "double precision", nullable: false),
                    AwayGoalAvg = table.Column<double>(type: "double precision", nullable: false),
                    HtHomeGoal = table.Column<int>(type: "integer", nullable: false),
                    HtAwayGoal = table.Column<int>(type: "integer", nullable: false),
                    HtHomeGoalAvg = table.Column<double>(type: "double precision", nullable: false),
                    HtAwayGoalAvg = table.Column<double>(type: "double precision", nullable: false),
                    HomeShots = table.Column<int>(type: "integer", nullable: false),
                    AwayShots = table.Column<int>(type: "integer", nullable: false),
                    HomeShotsOnTarget = table.Column<int>(type: "integer", nullable: false),
                    AwayShotsOnTarget = table.Column<int>(type: "integer", nullable: false),
                    HomeBallPossession = table.Column<int>(type: "integer", nullable: true),
                    AwayBallPossession = table.Column<int>(type: "integer", nullable: true),
                    HomePassesAccurate = table.Column<int>(type: "integer", nullable: true),
                    AwayPassesAccurate = table.Column<int>(type: "integer", nullable: true),
                    HomeXg = table.Column<double>(type: "double precision", nullable: false),
                    AwayXg = table.Column<double>(type: "double precision", nullable: false),
                    HomeWinOdds = table.Column<double>(type: "double precision", nullable: true),
                    DrawOdds = table.Column<double>(type: "double precision", nullable: true),
                    AwayWinOdds = table.Column<double>(type: "double precision", nullable: true),
                    Over25Odds = table.Column<double>(type: "double precision", nullable: true),
                    Under25Odds = table.Column<double>(type: "double precision", nullable: true),
                    BttsYesOdds = table.Column<double>(type: "double precision", nullable: true),
                    HomeElo = table.Column<double>(type: "double precision", nullable: true),
                    AwayElo = table.Column<double>(type: "double precision", nullable: true),
                    IsCurrentSeason = table.Column<bool>(type: "boolean", nullable: false),
                    IsDerby = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HomeRedCards = table.Column<int>(type: "integer", nullable: false),
                    AwayRedCards = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fixtures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fixtures_Teams_HomeTeamId",
                        column: x => x.HomeTeamId,
                        principalTable: "Teams",
                        principalColumn: "ApiId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fixtures_Teams_AwayTeamId",
                        column: x => x.AwayTeamId,
                        principalTable: "Teams",
                        principalColumn: "ApiId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FixtureAnalyses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    Lang = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    HomeProb = table.Column<double>(type: "double precision", nullable: false),
                    DrawProb = table.Column<double>(type: "double precision", nullable: false),
                    AwayProb = table.Column<double>(type: "double precision", nullable: false),
                    Over25Prob = table.Column<double>(type: "double precision", nullable: false),
                    BttsProb = table.Column<double>(type: "double precision", nullable: false),
                    PredictionReason = table.Column<string>(type: "text", nullable: false),
                    Analysis = table.Column<string>(type: "text", nullable: false),
                    TrapDetected = table.Column<bool>(type: "boolean", nullable: false),
                    TrapReason = table.Column<string>(type: "text", nullable: true),
                    ConsensusEvaluation = table.Column<string>(type: "text", nullable: false),
                    BttsSummary = table.Column<string>(type: "text", nullable: true),
                    Over25Summary = table.Column<string>(type: "text", nullable: true),
                    Under25Summary = table.Column<string>(type: "text", nullable: true),
                    HomeWinSummary = table.Column<string>(type: "text", nullable: true),
                    AwayWinSummary = table.Column<string>(type: "text", nullable: true),
                    AiOver25Qualified = table.Column<bool>(type: "boolean", nullable: false),
                    AiBttsQualified = table.Column<bool>(type: "boolean", nullable: false),
                    AiUnder25Qualified = table.Column<bool>(type: "boolean", nullable: false),
                    AiGoals23Qualified = table.Column<bool>(type: "boolean", nullable: false),
                    AiHomeWinQualified = table.Column<bool>(type: "boolean", nullable: false),
                    AiAwayWinQualified = table.Column<bool>(type: "boolean", nullable: false),
                    AiBestBet = table.Column<string>(type: "text", nullable: false),
                    AiOverallConfidence = table.Column<int>(type: "integer", nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixtureAnalyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixtureAnalyses_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Combinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TotalOdds = table.Column<double>(type: "double precision", nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    IsDailyCache = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Combinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BacktestReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WeeksBack = table.Column<int>(type: "integer", nullable: false),
                    Stake = table.Column<double>(type: "double precision", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastSuccessfulSyncUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunStartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCompletedStep = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                });

            // ── Indexes ──
            migrationBuilder.CreateIndex(name: "IX_Teams_ApiId", table: "Teams", column: "ApiId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Fixtures_ApiId", table: "Fixtures", column: "ApiId", unique: true);
            migrationBuilder.CreateIndex(name: "IX_Fixtures_HomeTeamId", table: "Fixtures", column: "HomeTeamId");
            migrationBuilder.CreateIndex(name: "IX_Fixtures_AwayTeamId", table: "Fixtures", column: "AwayTeamId");
            migrationBuilder.CreateIndex(
                name: "IX_FixtureAnalyses_FixtureId_Lang", table: "FixtureAnalyses",
                columns: ["FixtureId", "Lang"], unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_Combinations_Date_Language_IsDailyCache", table: "Combinations",
                columns: ["Date", "Language", "IsDailyCache"]);
            migrationBuilder.CreateIndex(
                name: "IX_BacktestReports_WeeksBack_Stake_CreatedAt", table: "BacktestReports",
                columns: ["WeeksBack", "Stake", "CreatedAt"]);
            migrationBuilder.CreateIndex(name: "IX_Users_Username", table: "Users", column: "Username", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FixtureAnalyses");
            migrationBuilder.DropTable(name: "Combinations");
            migrationBuilder.DropTable(name: "Users");
            migrationBuilder.DropTable(name: "BacktestReports");
            migrationBuilder.DropTable(name: "Fixtures");
            migrationBuilder.DropTable(name: "Teams");
            migrationBuilder.DropTable(name: "SyncStates");
        }
    }
}
