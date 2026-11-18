using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoulou.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SchuleEssaiPerioden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 18.11.2026 Probezeit: Standard 0 – der Admin (oder der Responsable der Schule) legt sie je Schule fest.
            migrationBuilder.AddColumn<int>(
                name: "EssaiPerioden",
                table: "Schulen",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EssaiPerioden",
                table: "Schulen");
        }
    }
}
