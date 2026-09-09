using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <summary>
    /// PostgreSQL counterpart of AddPredictionSnapshots: the prediction ledger,
    /// plus the odds/statistics freshness columns the live-odds revalidation
    /// depends on.
    /// </summary>
    /// <remarks>
    /// Written by hand. This folder had no PostgresDbContextModelSnapshot under
    /// source control, so the scaffolder had no baseline to diff against and
    /// emitted the ENTIRE schema — twelve CreateTable calls including Fixtures
    /// and FixtureOddsQuotes, all of which 20260714130000_InitPostgres already
    /// creates. Applying that would have failed immediately against the live
    /// database.
    ///
    /// The generated snapshot IS kept alongside this migration. It is the
    /// baseline that was missing, and committing it is what stops the next
    /// scaffold reproducing the same full-schema output.
    ///
    /// Kept deliberately in step with the SQLite migration of the same name —
    /// the two providers share one model, so a column added to one and not the
    /// other is a bug that only shows up after a provider switch.
    /// </remarks>
    public partial class AddPredictionSnapshotsPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "HomeObservedXg",
                table: "Fixtures",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AwayObservedXg",
                table: "Fixtures",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OddsCheckedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OddsUpdatedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatisticsUpdatedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderUpdatedAtUtc",
                table: "FixtureOddsQuotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PredictionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CaptureWindow = table.Column<long>(type: "bigint", nullable: false),
                    KickoffUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContextJson = table.Column<string>(type: "text", nullable: false),
                    Winner = table.Column<string>(type: "text", nullable: false),
                    Over25Pick = table.Column<bool>(type: "boolean", nullable: false),
                    BttsPick = table.Column<bool>(type: "boolean", nullable: false),
                    Goals23Pick = table.Column<bool>(type: "boolean", nullable: false),
                    Home = table.Column<double>(type: "double precision", nullable: false),
                    Draw = table.Column<double>(type: "double precision", nullable: false),
                    Away = table.Column<double>(type: "double precision", nullable: false),
                    Over25 = table.Column<double>(type: "double precision", nullable: false),
                    Btts = table.Column<double>(type: "double precision", nullable: false),
                    Goals23 = table.Column<double>(type: "double precision", nullable: false),
                    RawHome = table.Column<double>(type: "double precision", nullable: false),
                    RawDraw = table.Column<double>(type: "double precision", nullable: false),
                    RawAway = table.Column<double>(type: "double precision", nullable: false),
                    RawOver25 = table.Column<double>(type: "double precision", nullable: false),
                    RawBtts = table.Column<double>(type: "double precision", nullable: false),
                    RawGoals23 = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionSnapshots", x => x.Id);
                });

            // Unique: one row per fixture per 3h capture window. This is what
            // makes a simultaneous English/German/worker capture idempotent —
            // PredictionLedger relies on the constraint, not on a read-then-write.
            migrationBuilder.CreateIndex(
                name: "IX_PredictionSnapshots_FixtureId_CaptureWindow",
                table: "PredictionSnapshots",
                columns: new[] { "FixtureId", "CaptureWindow" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PredictionSnapshots_KickoffUtc",
                table: "PredictionSnapshots",
                column: "KickoffUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PredictionSnapshots");

            migrationBuilder.DropColumn(
                name: "ProviderUpdatedAtUtc",
                table: "FixtureOddsQuotes");

            migrationBuilder.DropColumn(
                name: "StatisticsUpdatedAtUtc",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "OddsUpdatedAtUtc",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "OddsCheckedAtUtc",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "AwayObservedXg",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "HomeObservedXg",
                table: "Fixtures");
        }
    }
}
