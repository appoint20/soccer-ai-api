using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFixtureInjuriesPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InjuriesCheckedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FixtureInjuries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    TeamApiId = table.Column<int>(type: "integer", nullable: false),
                    PlayerApiId = table.Column<int>(type: "integer", nullable: false),
                    PlayerName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KickoffUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixtureInjuries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixtureInjuries_FixtureId",
                table: "FixtureInjuries",
                column: "FixtureId");

            migrationBuilder.CreateIndex(
                name: "IX_FixtureInjuries_FixtureId_PlayerApiId_CapturedAtUtc",
                table: "FixtureInjuries",
                columns: new[] { "FixtureId", "PlayerApiId", "CapturedAtUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixtureInjuries");

            migrationBuilder.DropColumn(
                name: "InjuriesCheckedAtUtc",
                table: "Fixtures");
        }
    }
}
