using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class BestandsAbgaengeUndGeldbewegungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeldBewegungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Ort = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Herkunft = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    KassenSitzungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErfasstVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeldBewegungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeldBewegungen_KassenSitzungen_KassenSitzungId",
                        column: x => x.KassenSitzungId,
                        principalTable: "KassenSitzungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GeldBewegungen_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LagerAbgaenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Menge = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Grund = table.Column<int>(type: "int", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EinkaufspreisSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VerkaufspreisSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    KassenSitzungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErfasstVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LagerAbgaenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LagerAbgaenge_KassenSitzungen_KassenSitzungId",
                        column: x => x.KassenSitzungId,
                        principalTable: "KassenSitzungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LagerAbgaenge_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LagerAbgaenge_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeldBewegungen_KassenSitzungId",
                table: "GeldBewegungen",
                column: "KassenSitzungId");

            migrationBuilder.CreateIndex(
                name: "IX_GeldBewegungen_SalonId_ZeitUtc",
                table: "GeldBewegungen",
                columns: new[] { "SalonId", "ZeitUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LagerAbgaenge_KassenSitzungId",
                table: "LagerAbgaenge",
                column: "KassenSitzungId");

            migrationBuilder.CreateIndex(
                name: "IX_LagerAbgaenge_LeistungId",
                table: "LagerAbgaenge",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_LagerAbgaenge_SalonId_ZeitUtc",
                table: "LagerAbgaenge",
                columns: new[] { "SalonId", "ZeitUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeldBewegungen");

            migrationBuilder.DropTable(
                name: "LagerAbgaenge");
        }
    }
}
