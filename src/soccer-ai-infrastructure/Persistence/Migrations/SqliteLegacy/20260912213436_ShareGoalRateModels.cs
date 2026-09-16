using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <inheritdoc />
    public partial class ShareGoalRateModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoalRateModelGenerations",
                columns: table => new
                {
                    Generation = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    CalibrationJson = table.Column<string>(type: "TEXT", nullable: false),
                    EvaluationJson = table.Column<string>(type: "TEXT", nullable: false),
                    HomeModel = table.Column<byte[]>(type: "BLOB", nullable: false),
                    AwayModel = table.Column<byte[]>(type: "BLOB", nullable: false)
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
