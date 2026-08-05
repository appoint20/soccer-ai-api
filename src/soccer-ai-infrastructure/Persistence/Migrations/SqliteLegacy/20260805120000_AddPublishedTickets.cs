using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The live results ledger: what was published, at what price, and how it
    /// finished. Additive only — no existing table or column is touched.
    /// </summary>
    public partial class AddPublishedTickets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublishedTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BoardDateUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TotalOdds = table.Column<double>(type: "REAL", nullable: false),
                    CombinedProbability = table.Column<double>(type: "REAL", nullable: false),
                    Ev = table.Column<double>(type: "REAL", nullable: false),
                    KellyStake = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PublishedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    SettledAtUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_PublishedTickets", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PublishedTicketLegs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PublishedTicketId = table.Column<int>(type: "INTEGER", nullable: false),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    League = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Market = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Selection = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Probability = table.Column<double>(type: "REAL", nullable: false),
                    Odds = table.Column<double>(type: "REAL", nullable: false),
                    Ev = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishedTicketLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishedTicketLegs_PublishedTickets_PublishedTicketId",
                        column: x => x.PublishedTicketId,
                        principalTable: "PublishedTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Republishing a board must not duplicate a ticket.
            migrationBuilder.CreateIndex(
                name: "IX_PublishedTickets_Fingerprint",
                table: "PublishedTickets",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublishedTickets_BoardDateUtc_Status",
                table: "PublishedTickets",
                columns: new[] { "BoardDateUtc", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PublishedTicketLegs_PublishedTicketId",
                table: "PublishedTicketLegs",
                column: "PublishedTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_PublishedTicketLegs_FixtureId",
                table: "PublishedTicketLegs",
                column: "FixtureId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PublishedTicketLegs");
            migrationBuilder.DropTable(name: "PublishedTickets");
        }
    }
}
