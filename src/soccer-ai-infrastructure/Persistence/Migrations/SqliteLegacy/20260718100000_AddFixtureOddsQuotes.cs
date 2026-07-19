using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFixtureOddsQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FixtureOddsQuotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Bookmaker = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Market = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Price = table.Column<double>(type: "REAL", nullable: false),
                    CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
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
                columns: new[] { "FixtureId", "Market" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FixtureOddsQuotes");
        }
    }
}
