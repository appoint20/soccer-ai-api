using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <inheritdoc />
    public partial class RecordAiProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AiGeneratedAtUtc",
                table: "FixtureAnalyses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiInputHash",
                table: "FixtureAnalyses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModelVersion",
                table: "FixtureAnalyses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiPromptHash",
                table: "FixtureAnalyses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiGeneratedAtUtc",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiInputHash",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiModelVersion",
                table: "FixtureAnalyses");

            migrationBuilder.DropColumn(
                name: "AiPromptHash",
                table: "FixtureAnalyses");
        }
    }
}
