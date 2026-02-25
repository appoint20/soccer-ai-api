using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soccer_gpt_infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceFixtureTeamConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_AwayTeamId",
                table: "Fixtures",
                column: "AwayTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Fixtures_HomeTeamId",
                table: "Fixtures",
                column: "HomeTeamId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Fixtures_HomeAwayDifferent",
                table: "Fixtures",
                sql: "\"HomeTeamId\" <> \"AwayTeamId\"");

            migrationBuilder.AddForeignKey(
                name: "FK_Fixtures_Teams_AwayTeamId",
                table: "Fixtures",
                column: "AwayTeamId",
                principalTable: "Teams",
                principalColumn: "ApiId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Fixtures_Teams_HomeTeamId",
                table: "Fixtures",
                column: "HomeTeamId",
                principalTable: "Teams",
                principalColumn: "ApiId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Fixtures_Teams_AwayTeamId",
                table: "Fixtures");

            migrationBuilder.DropForeignKey(
                name: "FK_Fixtures_Teams_HomeTeamId",
                table: "Fixtures");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_AwayTeamId",
                table: "Fixtures");

            migrationBuilder.DropIndex(
                name: "IX_Fixtures_HomeTeamId",
                table: "Fixtures");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Fixtures_HomeAwayDifferent",
                table: "Fixtures");
        }
    }
}
