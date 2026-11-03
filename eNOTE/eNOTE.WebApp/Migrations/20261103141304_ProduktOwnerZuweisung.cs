using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class ProduktOwnerZuweisung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProduktOwnerSalons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KannLadenUmsatzSehen = table.Column<bool>(type: "bit", nullable: false),
                    KannPlattformUmsatzSehen = table.Column<bool>(type: "bit", nullable: false),
                    SelbstAngelegt = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProduktOwnerSalons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerSalons_AspNetUsers_ProduktOwnerId",
                        column: x => x.ProduktOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerSalons_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerSalons_ProduktOwnerId_SalonId",
                table: "ProduktOwnerSalons",
                columns: new[] { "ProduktOwnerId", "SalonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerSalons_SalonId",
                table: "ProduktOwnerSalons",
                column: "SalonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProduktOwnerSalons");
        }
    }
}
