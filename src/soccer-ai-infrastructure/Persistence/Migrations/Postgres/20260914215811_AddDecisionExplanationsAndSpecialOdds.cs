using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddDecisionExplanationsAndSpecialOdds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BttsAndOver25Odds",
                table: "Fixtures",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Goals23Odds",
                table: "Fixtures",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OddsBookmaker",
                table: "Fixtures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AiBttsAndOver25Qualified",
                table: "FixtureAnalyses",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionExplanationJson",
                table: "FixtureAnalyses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BttsAndOver25Odds",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "Goals23Odds",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "OddsBookmaker",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "AiBttsAndOver25Qualified",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "DecisionExplanationJson",
                table: "FixtureAnalyses");
        }
    }
}
