using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContextualIntelligenceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ManagerAppointedAt",
                table: "Teams",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagerName",
                table: "Teams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AwayRedCards",
                table: "Fixtures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HomeRedCards",
                table: "Fixtures",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagerAppointedAt",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ManagerName",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "AwayRedCards",
                table: "Fixtures");

            migrationBuilder.DropColumn(
                name: "HomeRedCards",
                table: "Fixtures");
        }
    }
}
