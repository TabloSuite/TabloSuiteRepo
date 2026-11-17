using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class PoProvisionen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PoProvisionen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NutzungsAboId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Laden = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ZeitraumVonUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZeitraumBisUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZahlungBestaetigtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AboBetrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProzentProvision = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FestProvision = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerseeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VersementReferenz = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VersementVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VersementErfasstUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErhaltenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NichtErhaltenHinweis = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoProvisionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoProvisionen_AspNetUsers_ProduktOwnerId",
                        column: x => x.ProduktOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoProvisionen_ProduktOwnerId_NutzungsAboId",
                table: "PoProvisionen",
                columns: new[] { "ProduktOwnerId", "NutzungsAboId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoProvisionen_ProduktOwnerId_SalonId_ZahlungBestaetigtUtc",
                table: "PoProvisionen",
                columns: new[] { "ProduktOwnerId", "SalonId", "ZahlungBestaetigtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PoProvisionen");
        }
    }
}
