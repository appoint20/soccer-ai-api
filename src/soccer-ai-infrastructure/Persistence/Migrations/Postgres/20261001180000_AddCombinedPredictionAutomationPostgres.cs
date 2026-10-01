using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres;

[DbContext(typeof(PostgresDbContext))]
[Migration("20261001180000_AddCombinedPredictionAutomationPostgres")]
public sealed class AddCombinedPredictionAutomationPostgres : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("CombinedPredictionAutomationAttempts", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            FixtureId = table.Column<int>(type: "integer", nullable: false),
            KickoffUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            Window = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            SnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
            Error = table.Column<string>(type: "text", nullable: true)
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
