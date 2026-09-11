using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RecordAiProvenancePostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiGeneratedAtUtc",
                table: "FixtureAnalyses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiInputHash",
                table: "FixtureAnalyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiModelVersion",
                table: "FixtureAnalyses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiPromptHash",
                table: "FixtureAnalyses",
                type: "text",
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
