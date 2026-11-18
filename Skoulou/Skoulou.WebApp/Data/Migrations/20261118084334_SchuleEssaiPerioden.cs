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
            // 18.11.2026 Probezeit: bestehende Schulen 0 (ihre Abrechnung bleibt unverändert), neue Schulen 1 (Entität).
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
