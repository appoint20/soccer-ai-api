using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameToFixtureAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FixtureAnalyses_FixtureApiId_Lang",
                table: "FixtureAnalyses");

            migrationBuilder.RenameColumn(
                name: "FixtureApiId",
                table: "FixtureAnalyses",
                newName: "FixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_FixtureAnalyses_FixtureId_Lang",
                table: "FixtureAnalyses",
                columns: new[] { "FixtureId", "Lang" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FixtureAnalyses_FixtureId_Lang",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "ConsensusEvaluation",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "Lang",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "TrapDetected",
                table: "FixtureAnalyses");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "FixtureAnalyses",
                newName: "Trap");

            migrationBuilder.RenameColumn(
                name: "PredictionReason",
                table: "FixtureAnalyses",
                newName: "Reasoning");

            migrationBuilder.CreateIndex(
                name: "IX_FixtureAnalyses_FixtureId",
                table: "FixtureAnalyses",
                column: "FixtureId");
        }
    }
}
