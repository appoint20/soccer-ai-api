using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <inheritdoc />
    public partial class AddFixtureInjuries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "InjuriesCheckedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FixtureInjuries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FixtureId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamApiId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerApiId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CapturedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    KickoffUtc = table.Column<long>(type: "INTEGER", nullable: false)
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
