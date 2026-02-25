using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace soccer_gpt_infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGranularGeminiSummariesDirectVFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeminiAwayWinSummary",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeminiBttsSummary",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeminiHomeWinSummary",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeminiOneLineSummary",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeminiOver25Summary",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeminiTrapReason",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeminiUnder25Summary",
                table: "Fixtures",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeminiAwayWinSummary",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "GeminiBttsSummary",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "GeminiHomeWinSummary",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "GeminiOneLineSummary",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "GeminiOver25Summary",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "GeminiTrapReason",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "GeminiUnder25Summary",
                table: "Fixtures");
        }
    }
}
