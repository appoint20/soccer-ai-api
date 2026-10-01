using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930220001_AddCombinedPredictionSnapshots")]
public sealed class AddCombinedPredictionSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("CombinedPredictionSnapshots", columns: table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
            KickoffUtc = table.Column<long>(type: "INTEGER", nullable: false),
            CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
            PredictionJson = table.Column<string>(type: "TEXT", nullable: false),
            EvidenceJson = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CombinedPredictionSnapshots", row => row.Id);
            table.ForeignKey("FK_CombinedPredictionSnapshots_Fixtures_FixtureId", row => row.FixtureId,
                "Fixtures", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_CombinedPredictionSnapshots_FixtureId_CapturedAtUtc",
            "CombinedPredictionSnapshots", new[] { "FixtureId", "CapturedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("CombinedPredictionSnapshots");
}
