using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Head-to-head forecast records: one row per fixture per language model,
    /// carrying the pipeline's forecast frozen at the same instant and the
    /// settled result. Additive only — no existing table or column is touched.
    /// </summary>
    /// <remarks>
    /// Both attributes are required. EF discovers migrations by the
    /// <see cref="MigrationAttribute"/> and matches them to a context by
    /// <see cref="DbContextAttribute"/>; the other SQLite migrations carry these
    /// in generated .Designer.cs companions. Without them this class is
    /// invisible — it applies silently to nothing, and the missing table only
    /// surfaces later as "no such table: ModelForecasts".
    /// </remarks>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260814090000_AddModelForecasts")]
    public partial class AddModelForecasts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelForecasts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),

                    // DateTimeOffset is stored as UTC ticks on SQLite — see the
                    // converter in ApplicationDbContext.OnModelCreating.
                    PredictedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    KickoffUtc = table.Column<long>(type: "INTEGER", nullable: false),

                    ExpectedGoals = table.Column<double>(type: "REAL", nullable: false),
                    PredictedHomeGoals = table.Column<int>(type: "INTEGER", nullable: false),
                    PredictedAwayGoals = table.Column<int>(type: "INTEGER", nullable: false),
                    Over25Probability = table.Column<double>(type: "REAL", nullable: false),
                    BttsProbability = table.Column<double>(type: "REAL", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),

                    SystemExpectedGoals = table.Column<double>(type: "REAL", nullable: false),
                    SystemOver25Probability = table.Column<double>(type: "REAL", nullable: false),
                    SystemBttsProbability = table.Column<double>(type: "REAL", nullable: false),

                    SettledAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    ActualHomeGoals = table.Column<int>(type: "INTEGER", nullable: true),
                    ActualAwayGoals = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_ModelForecasts", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_ModelForecasts_FixtureId_Model",
                table: "ModelForecasts",
                columns: ["FixtureId", "Model"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelForecasts_SettledAtUtc_KickoffUtc",
                table: "ModelForecasts",
                columns: ["SettledAtUtc", "KickoffUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_ModelForecasts_Model_SettledAtUtc",
                table: "ModelForecasts",
                columns: ["Model", "SettledAtUtc"]);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ModelForecasts");
        }
    }
}
