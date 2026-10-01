using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261001180001_AddCombinedPredictionAutomation")]
public sealed class AddCombinedPredictionAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("CombinedPredictionAutomationAttempts", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
            KickoffUtc = table.Column<long>(type: "INTEGER", nullable: false),
            Window = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            StartedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
            FinishedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
            Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
            SnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
            Error = table.Column<string>(type: "TEXT", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CombinedPredictionAutomationAttempts", row => row.Id);
            table.ForeignKey("FK_CombinedPredictionAutomationAttempts_Fixtures_FixtureId", row => row.FixtureId,
                "Fixtures", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_CombinedAutomation_Fixture_Kickoff_Window",
            "CombinedPredictionAutomationAttempts", new[] { "FixtureId", "KickoffUtc", "Window" }, unique: true);
        migrationBuilder.CreateIndex("IX_CombinedPredictionAutomationAttempts_StartedAtUtc",
            "CombinedPredictionAutomationAttempts", "StartedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("CombinedPredictionAutomationAttempts");
}
