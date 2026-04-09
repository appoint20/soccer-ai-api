using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktestReportCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BacktestReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WeeksBack = table.Column<int>(type: "INTEGER", nullable: false),
                    Stake = table.Column<double>(type: "REAL", nullable: false),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BacktestReports_WeeksBack_Stake_CreatedAt",
                table: "BacktestReports",
                columns: new[] { "WeeksBack", "Stake", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestReports");
        }
    }
}
