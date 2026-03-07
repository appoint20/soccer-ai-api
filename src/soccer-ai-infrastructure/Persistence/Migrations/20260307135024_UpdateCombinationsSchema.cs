using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCombinationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Date",
                table: "Combinations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDailyCache",
                table: "Combinations",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Combinations",
                type: "TEXT",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "Combinations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Combinations_Date_Language_IsDailyCache",
                table: "Combinations",
                columns: new[] { "Date", "Language", "IsDailyCache" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Combinations_Date_Language_IsDailyCache",
                table: "Combinations");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "Combinations");

            migrationBuilder.DropColumn(
                name: "IsDailyCache",
                table: "Combinations");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Combinations");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "Combinations");
        }
    }
}
