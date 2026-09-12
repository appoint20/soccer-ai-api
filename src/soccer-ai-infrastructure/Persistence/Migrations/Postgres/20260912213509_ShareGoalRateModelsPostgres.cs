using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class ShareGoalRateModelsPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalRateModelGenerations",
                columns: table => new
                {
                    Generation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    CalibrationJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationJson = table.Column<string>(type: "text", nullable: false),
                    HomeModel = table.Column<byte[]>(type: "bytea", nullable: false),
                    AwayModel = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalRateModelGenerations", x => x.Generation);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoalRateModelGenerations_CreatedAtUtc",
                table: "GoalRateModelGenerations",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoalRateModelGenerations");
        }
    }
}
