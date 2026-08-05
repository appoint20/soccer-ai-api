using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <summary>
    /// The live results ledger: what was published, at what price, and how it
    /// finished. Additive only — no existing table or column is touched.
    /// </summary>
    [DbContext(typeof(PostgresDbContext))]
    [Migration("20260805120000_AddPublishedTicketsPostgres")]
    public partial class AddPublishedTicketsPostgres : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublishedTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BoardDateUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalOdds = table.Column<double>(type: "double precision", nullable: false),
                    CombinedProbability = table.Column<double>(type: "double precision", nullable: false),
                    Ev = table.Column<double>(type: "double precision", nullable: false),
                    KellyStake = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SettledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_PublishedTickets", x => x.Id));

            migrationBuilder.CreateTable(
                name: "PublishedTicketLegs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PublishedTicketId = table.Column<int>(type: "integer", nullable: false),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    League = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Market = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Selection = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Probability = table.Column<double>(type: "double precision", nullable: false),
                    Odds = table.Column<double>(type: "double precision", nullable: false),
                    Ev = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
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
                columns: ["BoardDateUtc", "Status"]);

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
