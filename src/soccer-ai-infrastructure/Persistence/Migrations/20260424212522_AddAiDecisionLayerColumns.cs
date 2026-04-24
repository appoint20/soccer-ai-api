using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDecisionLayerColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AiAwayWinQualified",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AiBestBet",
                table: "FixtureAnalyses",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "AiBttsQualified",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiGoals23Qualified",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiHomeWinQualified",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AiOver25Qualified",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AiOverallConfidence",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AiUnder25Qualified",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiAwayWinQualified",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiBestBet",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiBttsQualified",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiGoals23Qualified",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiHomeWinQualified",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiOver25Qualified",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiOverallConfidence",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiUnder25Qualified",
                table: "FixtureAnalyses");
        }
    }
}
