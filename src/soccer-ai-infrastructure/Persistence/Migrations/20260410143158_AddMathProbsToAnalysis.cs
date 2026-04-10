using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMathProbsToAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AwayProb",
                table: "FixtureAnalyses",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "BttsProb",
                table: "FixtureAnalyses",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DrawProb",
                table: "FixtureAnalyses",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "HomeProb",
                table: "FixtureAnalyses",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Over25Prob",
                table: "FixtureAnalyses",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayProb",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "BttsProb",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "DrawProb",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "HomeProb",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "Over25Prob",
                table: "FixtureAnalyses");
        }
    }
}
