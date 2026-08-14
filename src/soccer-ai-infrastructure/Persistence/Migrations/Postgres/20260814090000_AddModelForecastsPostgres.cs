using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <summary>
    /// Head-to-head forecast records: one row per fixture per language model,
    /// carrying the pipeline's forecast frozen at the same instant and the
    /// settled result. Additive only — no existing table or column is touched.
    /// </summary>
    [DbContext(typeof(PostgresDbContext))]
    [Migration("20260814090000_AddModelForecastsPostgres")]
    public partial class AddModelForecastsPostgres : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelForecasts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PredictedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KickoffUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),

                    ExpectedGoals = table.Column<double>(type: "double precision", nullable: false),
                    PredictedHomeGoals = table.Column<int>(type: "integer", nullable: false),
                    PredictedAwayGoals = table.Column<int>(type: "integer", nullable: false),
                    Over25Probability = table.Column<double>(type: "double precision", nullable: false),
                    BttsProbability = table.Column<double>(type: "double precision", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Rationale = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),

                    SystemExpectedGoals = table.Column<double>(type: "double precision", nullable: false),
                    SystemOver25Probability = table.Column<double>(type: "double precision", nullable: false),
                    SystemBttsProbability = table.Column<double>(type: "double precision", nullable: false),

                    SettledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualHomeGoals = table.Column<int>(type: "integer", nullable: true),
                    ActualAwayGoals = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_ModelForecasts", x => x.Id));

            // Re-running a sync updates a fixture's forecast for a model rather
            // than accumulating a second opinion from the same one.
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
