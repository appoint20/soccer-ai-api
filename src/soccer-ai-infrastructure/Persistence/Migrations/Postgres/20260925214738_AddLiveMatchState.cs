using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SoccerAi.Infrastructure.Persistence.Migrations.Postgres
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
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraMinutes",
                table: "Fixtures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveCheckedAtUtc",
                table: "Fixtures",
                type: "timestamp with time zone",
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
