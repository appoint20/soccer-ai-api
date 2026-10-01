using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres;

[DbContext(typeof(PostgresDbContext))]
[Migration("20260930220000_AddCombinedPredictionSnapshotsPostgres")]
public sealed class AddCombinedPredictionSnapshotsPostgres : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("CombinedPredictionSnapshots", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            FixtureId = table.Column<int>(type: "integer", nullable: false),
            KickoffUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            PredictionJson = table.Column<string>(type: "text", nullable: false),
            EvidenceJson = table.Column<string>(type: "text", nullable: false)
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
