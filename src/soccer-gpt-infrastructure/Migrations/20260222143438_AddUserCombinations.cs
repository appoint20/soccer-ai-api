using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soccer_gpt_infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserCombinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserCombinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalOdds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCombinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserCombinationMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserCombinationId = table.Column<int>(type: "INTEGER", nullable: false),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    Market = table.Column<string>(type: "TEXT", nullable: false),
                    Prediction = table.Column<string>(type: "TEXT", nullable: false),
                    Odds = table.Column<double>(type: "REAL", nullable: false),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCombinationMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCombinationMatches_UserCombinations_UserCombinationId",
                        column: x => x.UserCombinationId,
                        principalTable: "UserCombinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserCombinationMatches_UserCombinationId",
                table: "UserCombinationMatches",
                column: "UserCombinationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCombinationMatches");

            migrationBuilder.DropTable(
                name: "UserCombinations");
        }
    }
}
