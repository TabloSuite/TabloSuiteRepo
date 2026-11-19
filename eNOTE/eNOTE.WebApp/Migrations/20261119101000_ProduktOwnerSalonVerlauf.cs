using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class ProduktOwnerSalonVerlauf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProduktOwnerSalons_ProduktOwnerId_SalonId",
                table: "ProduktOwnerSalons");

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerSalons_ProduktOwnerId_SalonId",
                table: "ProduktOwnerSalons",
                columns: new[] { "ProduktOwnerId", "SalonId" },
                unique: true,
                filter: "[GueltigBisUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProduktOwnerSalons_ProduktOwnerId_SalonId",
                table: "ProduktOwnerSalons");

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerSalons_ProduktOwnerId_SalonId",
                table: "ProduktOwnerSalons",
                columns: new[] { "ProduktOwnerId", "SalonId" },
                unique: true);
        }
    }
}
