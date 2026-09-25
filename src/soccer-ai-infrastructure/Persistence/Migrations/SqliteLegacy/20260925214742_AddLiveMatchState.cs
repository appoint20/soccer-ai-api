using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.SqliteLegacy
{
    /// <inheritdoc />
    public partial class AddLiveMatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ElapsedMinutes",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraMinutes",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LiveCheckedAtUtc",
                table: "Fixtures",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ElapsedMinutes",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "ExtraMinutes",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "LiveCheckedAtUtc",
                table: "Fixtures");
        }
    }
}
