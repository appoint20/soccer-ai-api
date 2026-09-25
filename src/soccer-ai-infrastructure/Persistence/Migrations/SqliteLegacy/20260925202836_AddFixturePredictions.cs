using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <inheritdoc />
    public partial class AddFixturePredictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PredictionCheckedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FixturePredictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    PercentHome = table.Column<double>(type: "REAL", nullable: true),
                    PercentDraw = table.Column<double>(type: "REAL", nullable: true),
                    PercentAway = table.Column<double>(type: "REAL", nullable: true),
                    Form = table.Column<double>(type: "REAL", nullable: true),
                    Attack = table.Column<double>(type: "REAL", nullable: true),
                    Defence = table.Column<double>(type: "REAL", nullable: true),
                    Poisson = table.Column<double>(type: "REAL", nullable: true),
                    HeadToHead = table.Column<double>(type: "REAL", nullable: true),
                    Goals = table.Column<double>(type: "REAL", nullable: true),
                    Total = table.Column<double>(type: "REAL", nullable: true),
                    Advice = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WinnerName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    UnderOver = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    HomePlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    HomeForm = table.Column<double>(type: "REAL", nullable: true),
                    HomeAttack = table.Column<double>(type: "REAL", nullable: true),
                    HomeDefence = table.Column<double>(type: "REAL", nullable: true),
                    HomeGoalsFor = table.Column<double>(type: "REAL", nullable: true),
                    HomeGoalsAgainst = table.Column<double>(type: "REAL", nullable: true),
                    AwayPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    AwayForm = table.Column<double>(type: "REAL", nullable: true),
                    AwayAttack = table.Column<double>(type: "REAL", nullable: true),
                    AwayDefence = table.Column<double>(type: "REAL", nullable: true),
                    AwayGoalsFor = table.Column<double>(type: "REAL", nullable: true),
                    AwayGoalsAgainst = table.Column<double>(type: "REAL", nullable: true),
                    CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixturePredictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixturePredictions_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixturePredictions_FixtureId",
                table: "FixturePredictions",
                column: "FixtureId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixturePredictions");

            migrationBuilder.DropColumn(
                name: "PredictionCheckedAtUtc",
                table: "Fixtures");
        }
    }
}
