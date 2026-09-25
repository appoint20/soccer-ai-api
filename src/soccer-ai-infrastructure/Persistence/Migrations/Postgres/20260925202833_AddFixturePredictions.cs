using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AddFixturePredictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PredictionCheckedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FixturePredictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FixtureId = table.Column<int>(type: "integer", nullable: false),
                    PercentHome = table.Column<double>(type: "double precision", nullable: true),
                    PercentDraw = table.Column<double>(type: "double precision", nullable: true),
                    PercentAway = table.Column<double>(type: "double precision", nullable: true),
                    Form = table.Column<double>(type: "double precision", nullable: true),
                    Attack = table.Column<double>(type: "double precision", nullable: true),
                    Defence = table.Column<double>(type: "double precision", nullable: true),
                    Poisson = table.Column<double>(type: "double precision", nullable: true),
                    HeadToHead = table.Column<double>(type: "double precision", nullable: true),
                    Goals = table.Column<double>(type: "double precision", nullable: true),
                    Total = table.Column<double>(type: "double precision", nullable: true),
                    Advice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WinnerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    UnderOver = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    HomePlayed = table.Column<int>(type: "integer", nullable: false),
                    HomeForm = table.Column<double>(type: "double precision", nullable: true),
                    HomeAttack = table.Column<double>(type: "double precision", nullable: true),
                    HomeDefence = table.Column<double>(type: "double precision", nullable: true),
                    HomeGoalsFor = table.Column<double>(type: "double precision", nullable: true),
                    HomeGoalsAgainst = table.Column<double>(type: "double precision", nullable: true),
                    AwayPlayed = table.Column<int>(type: "integer", nullable: false),
                    AwayForm = table.Column<double>(type: "double precision", nullable: true),
                    AwayAttack = table.Column<double>(type: "double precision", nullable: true),
                    AwayDefence = table.Column<double>(type: "double precision", nullable: true),
                    AwayGoalsFor = table.Column<double>(type: "double precision", nullable: true),
                    AwayGoalsAgainst = table.Column<double>(type: "double precision", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixturePredictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixturePredictions_Fixtures_FixtureId",
                        column: x => x.FixtureId,
                        principalTable: "Fixtures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixturePredictions_FixtureId",
                table: "FixturePredictions",
                column: "FixtureId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FixturePredictions");

            migrationBuilder.DropColumn(
                name: "PredictionCheckedAtUtc",
                table: "Fixtures");
        }
    }
}
