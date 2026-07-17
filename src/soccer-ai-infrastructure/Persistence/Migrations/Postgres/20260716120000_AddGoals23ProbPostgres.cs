using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <summary>
    /// Adds the 2-3 Goals probability to the math cache. Separate migration
    /// (instead of editing InitPostgres) so databases that already applied the
    /// initial migration upgrade cleanly.
    /// </summary>
    [DbContext(typeof(PostgresDbContext))]
    [Migration("20260716120000_AddGoals23ProbPostgres")]
    public partial class AddGoals23ProbPostgres : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Goals23Prob",
                table: "FixtureAnalyses",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Goals23Prob",
                table: "FixtureAnalyses");
        }
    }
}
