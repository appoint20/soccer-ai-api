using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <summary>
    /// The prediction ledger, plus the odds/statistics freshness columns the
    /// live-odds revalidation depends on.
    /// </summary>
    /// <remarks>
    /// Hand-trimmed after scaffolding. The committed model snapshot had fallen
    /// behind the migrations on disk — it carried no ModelForecasts,
    /// PublishedTickets or PublishedTicketLegs — so EF re-emitted CreateTable
    /// for all three even though 20260805120000_AddPublishedTickets and
    /// 20260814090000_AddModelForecasts already create them. Applying that
    /// unedited would fail on any database those two had already reached.
    ///
    /// Only the genuinely new objects are kept here. The regenerated snapshot
    /// is committed alongside, which is what stops the next scaffold repeating
    /// this.
    /// </remarks>
    public partial class AddPredictionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AwayObservedXg",
                table: "Fixtures",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HomeObservedXg",
                table: "Fixtures",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OddsCheckedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OddsUpdatedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StatisticsUpdatedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProviderUpdatedAtUtc",
                table: "FixtureOddsQuotes",
                type: "INTEGER",
                nullable: true);


            migrationBuilder.CreateTable(
                name: "PredictionSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    CaptureWindow = table.Column<long>(type: "INTEGER", nullable: false),
                    KickoffUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ModelVersion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: false),
                    Winner = table.Column<string>(type: "TEXT", nullable: false),
                    Over25Pick = table.Column<bool>(type: "INTEGER", nullable: false),
                    BttsPick = table.Column<bool>(type: "INTEGER", nullable: false),
                    Goals23Pick = table.Column<bool>(type: "INTEGER", nullable: false),
                    Home = table.Column<double>(type: "REAL", nullable: false),
                    Draw = table.Column<double>(type: "REAL", nullable: false),
                    Away = table.Column<double>(type: "REAL", nullable: false),
                    Over25 = table.Column<double>(type: "REAL", nullable: false),
                    Btts = table.Column<double>(type: "REAL", nullable: false),
                    Goals23 = table.Column<double>(type: "REAL", nullable: false),
                    RawHome = table.Column<double>(type: "REAL", nullable: false),
                    RawDraw = table.Column<double>(type: "REAL", nullable: false),
                    RawAway = table.Column<double>(type: "REAL", nullable: false),
                    RawOver25 = table.Column<double>(type: "REAL", nullable: false),
                    RawBtts = table.Column<double>(type: "REAL", nullable: false),
                    RawGoals23 = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PredictionSnapshots", x => x.Id);
                });






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
                name: "AwayObservedXg",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "HomeObservedXg",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "OddsCheckedAtUtc",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "OddsUpdatedAtUtc",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "StatisticsUpdatedAtUtc",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "ProviderUpdatedAtUtc",
                table: "FixtureOddsQuotes");
        }
    }
}
