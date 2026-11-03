using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AusgabenUndProvisionsAuszahlung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AusgabenKategorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AusgabenKategorien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AusgabenKategorien_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProduktOwnerAuszahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProduktOwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Jahr = table.Column<int>(type: "int", nullable: false),
                    Monat = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Aufschluesselung = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BestaetigtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProduktOwnerAuszahlungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProduktOwnerAuszahlungen_AspNetUsers_ProduktOwnerId",
                        column: x => x.ProduktOwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ausgaben",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DatumUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KategorieId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Notiz = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BelegAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ausgaben", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ausgaben_AusgabenKategorien_KategorieId",
                        column: x => x.KategorieId,
                        principalTable: "AusgabenKategorien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ausgaben_BlobMediaAssets_BelegAssetId",
                        column: x => x.BelegAssetId,
                        principalTable: "BlobMediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Ausgaben_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ausgaben_BelegAssetId",
                table: "Ausgaben",
                column: "BelegAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgaben_KategorieId",
                table: "Ausgaben",
                column: "KategorieId");

            migrationBuilder.CreateIndex(
                name: "IX_Ausgaben_SalonId",
                table: "Ausgaben",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_AusgabenKategorien_SalonId",
                table: "AusgabenKategorien",
                column: "SalonId");

            migrationBuilder.CreateIndex(
                name: "IX_ProduktOwnerAuszahlungen_ProduktOwnerId_Jahr_Monat",
                table: "ProduktOwnerAuszahlungen",
                columns: new[] { "ProduktOwnerId", "Jahr", "Monat" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ausgaben");

            migrationBuilder.DropTable(
                name: "ProduktOwnerAuszahlungen");

            migrationBuilder.DropTable(
                name: "AusgabenKategorien");
        }
    }
}
