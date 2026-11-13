using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AufenthaltsPositionen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ErbrachtAm",
                table: "VerkaufsPositionen",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AufenthaltsPositionen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BesuchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KundeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeistungName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EinzelPreis = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Menge = table.Column<int>(type: "int", nullable: false),
                    FreierBetrag = table.Column<bool>(type: "bit", nullable: false),
                    ErbrachtAm = table.Column<DateOnly>(type: "date", nullable: false),
                    ErfasstUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErfasstVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LagerLeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LagerMenge = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    VerkaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ZurueckgenommenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ZurueckgenommenVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RuecknahmeGrund = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    InsLagerZurueck = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AufenthaltsPositionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AufenthaltsPositionen_KundenBesuche_BesuchId",
                        column: x => x.BesuchId,
                        principalTable: "KundenBesuche",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AufenthaltsPositionen_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AufenthaltsPositionen_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AufenthaltsPositionen_BesuchId",
                table: "AufenthaltsPositionen",
                column: "BesuchId");

            migrationBuilder.CreateIndex(
                name: "IX_AufenthaltsPositionen_LeistungId",
                table: "AufenthaltsPositionen",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_AufenthaltsPositionen_SalonId_KundeId_VerkaufId",
                table: "AufenthaltsPositionen",
                columns: new[] { "SalonId", "KundeId", "VerkaufId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AufenthaltsPositionen");

            migrationBuilder.DropColumn(
                name: "ErbrachtAm",
                table: "VerkaufsPositionen");
        }
    }
}
