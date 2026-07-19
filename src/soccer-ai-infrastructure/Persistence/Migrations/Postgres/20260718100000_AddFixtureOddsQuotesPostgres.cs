using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <summary>Per-bookmaker odds quote history (line shopping + drift).</summary>
    [DbContext(typeof(PostgresDbContext))]
    [Migration("20260718100000_AddFixtureOddsQuotesPostgres")]
    public partial class AddFixtureOddsQuotesPostgres : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FixtureOddsQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    Bookmaker = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Market = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Price = table.Column<double>(type: "double precision", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixtureOddsQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixtureOddsQuotes_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixtureOddsQuotes_FixtureId_Market",
                table: "FixtureOddsQuotes",
                columns: ["FixtureId", "Market"]);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FixtureOddsQuotes");
        }
    }
}
