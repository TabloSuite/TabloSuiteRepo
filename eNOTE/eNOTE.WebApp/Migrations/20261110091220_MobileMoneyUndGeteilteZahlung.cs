using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class MobileMoneyUndGeteilteZahlung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MobileMoneyIndividuell",
                table: "Salons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MobileMoneyAnbieter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Land = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Farbe = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileMoneyAnbieter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalonMobileMoneyAnbieter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnbieterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalonMobileMoneyAnbieter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalonMobileMoneyAnbieter_MobileMoneyAnbieter_AnbieterId",
                        column: x => x.AnbieterId,
                        principalTable: "MobileMoneyAnbieter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SalonMobileMoneyAnbieter_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VerkaufZahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VerkaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MobileMoneyAnbieterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AnbieterName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Referenz = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerkaufZahlungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerkaufZahlungen_MobileMoneyAnbieter_MobileMoneyAnbieterId",
                        column: x => x.MobileMoneyAnbieterId,
                        principalTable: "MobileMoneyAnbieter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerkaufZahlungen_Verkaeufe_VerkaufId",
                        column: x => x.VerkaufId,
                        principalTable: "Verkaeufe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "MobileMoneyAnbieter",
                columns: new[] { "Id", "CreatedAtUtc", "Farbe", "IstAktiv", "Land", "Name", "Reihenfolge" },
                values: new object[,]
                {
                    { new Guid("7a0d0001-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#0b5cad", true, "Togo", "Flooz", 1 },
                    { new Guid("7a0d0001-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ffc800", true, "Togo", "Mixx by Yas", 2 },
                    { new Guid("7a0d0002-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ffcb05", true, "Bénin", "MTN MoMo", 1 },
                    { new Guid("7a0d0002-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#0b5cad", true, "Bénin", "Moov Money", 2 },
                    { new Guid("7a0d0002-0000-4000-8000-000000000003"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#d71920", true, "Bénin", "Celtiis Cash", 3 },
                    { new Guid("7a0d0003-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ff7900", true, "Sénégal", "Orange Money", 1 },
                    { new Guid("7a0d0003-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#1dc8f2", true, "Sénégal", "Wave", 2 },
                    { new Guid("7a0d0003-0000-4000-8000-000000000003"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ffc800", true, "Sénégal", "Mixx by Yas", 3 },
                    { new Guid("7a0d0004-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ff7900", true, "Côte d'Ivoire", "Orange Money", 1 },
                    { new Guid("7a0d0004-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ffcb05", true, "Côte d'Ivoire", "MTN MoMo", 2 },
                    { new Guid("7a0d0004-0000-4000-8000-000000000003"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#0b5cad", true, "Côte d'Ivoire", "Moov Money", 3 },
                    { new Guid("7a0d0004-0000-4000-8000-000000000004"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#1dc8f2", true, "Côte d'Ivoire", "Wave", 4 },
                    { new Guid("7a0d0005-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ff7900", true, "Burkina Faso", "Orange Money", 1 },
                    { new Guid("7a0d0005-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#0b5cad", true, "Burkina Faso", "Moov Money", 2 },
                    { new Guid("7a0d0006-0000-4000-8000-000000000001"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#ff7900", true, "Mali", "Orange Money", 1 },
                    { new Guid("7a0d0006-0000-4000-8000-000000000002"), new DateTime(2026, 11, 10, 0, 0, 0, 0, DateTimeKind.Utc), "#0b5cad", true, "Mali", "Moov Money", 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MobileMoneyAnbieter_Land_Name",
                table: "MobileMoneyAnbieter",
                columns: new[] { "Land", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalonMobileMoneyAnbieter_AnbieterId",
                table: "SalonMobileMoneyAnbieter",
                column: "AnbieterId");

            migrationBuilder.CreateIndex(
                name: "IX_SalonMobileMoneyAnbieter_SalonId_AnbieterId",
                table: "SalonMobileMoneyAnbieter",
                columns: new[] { "SalonId", "AnbieterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufZahlungen_MobileMoneyAnbieterId",
                table: "VerkaufZahlungen",
                column: "MobileMoneyAnbieterId");

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufZahlungen_VerkaufId_Reihenfolge",
                table: "VerkaufZahlungen",
                columns: new[] { "VerkaufId", "Reihenfolge" });

            // Bestandsübernahme: jeder vorhandene Verkauf erhält genau einen Zahlungsanteil
            // in Höhe seiner Gesamtsumme mit der bisherigen Zahlart (Bar = 0, Karte = 1).
            migrationBuilder.Sql("""
                INSERT INTO [VerkaufZahlungen] ([Id], [VerkaufId], [Art], [Betrag], [Reihenfolge])
                SELECT NEWID(), v.[Id], v.[ZahlungsArt], v.[GesamtSumme], 0
                FROM [Verkaeufe] AS v
                WHERE NOT EXISTS (SELECT 1 FROM [VerkaufZahlungen] AS z WHERE z.[VerkaufId] = v.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalonMobileMoneyAnbieter");

            migrationBuilder.DropTable(
                name: "VerkaufZahlungen");

            migrationBuilder.DropTable(
                name: "MobileMoneyAnbieter");

            migrationBuilder.DropColumn(
                name: "MobileMoneyIndividuell",
                table: "Salons");
        }
    }
}
