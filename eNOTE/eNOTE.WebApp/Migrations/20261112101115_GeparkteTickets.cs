using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class GeparkteTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeparkteTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KundeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RabattId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RabattBedingungBestaetigt = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GeaendertUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeparkteTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeparkteTickets_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GeparkteTickets_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GeparkteTicketPositionen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Menge = table.Column<int>(type: "int", nullable: false),
                    FreierBetrag = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeparkteTicketPositionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeparkteTicketPositionen_GeparkteTickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "GeparkteTickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GeparkteTicketPositionen_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeparkteTicketPositionen_LeistungId",
                table: "GeparkteTicketPositionen",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_GeparkteTicketPositionen_TicketId",
                table: "GeparkteTicketPositionen",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_GeparkteTickets_KundeId",
                table: "GeparkteTickets",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_GeparkteTickets_SalonId_GeaendertUtc",
                table: "GeparkteTickets",
                columns: new[] { "SalonId", "GeaendertUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeparkteTicketPositionen");

            migrationBuilder.DropTable(
                name: "GeparkteTickets");
        }
    }
}
