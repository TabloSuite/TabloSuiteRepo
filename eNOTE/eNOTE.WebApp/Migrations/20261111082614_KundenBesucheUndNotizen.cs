using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class KundenBesucheUndNotizen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KundenBesuche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KundeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grund = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Raum = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AnkunftUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VerkaufId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundenBesuche", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KundenBesuche_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KundenNotizen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KundeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BesuchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltVonName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KundenNotizen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KundenNotizen_KundenBesuche_BesuchId",
                        column: x => x.BesuchId,
                        principalTable: "KundenBesuche",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KundenNotizen_Kunden_KundeId",
                        column: x => x.KundeId,
                        principalTable: "Kunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KundenBesuche_KundeId_AnkunftUtc",
                table: "KundenBesuche",
                columns: new[] { "KundeId", "AnkunftUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_KundenBesuche_SalonId_EndeUtc",
                table: "KundenBesuche",
                columns: new[] { "SalonId", "EndeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_KundenNotizen_BesuchId",
                table: "KundenNotizen",
                column: "BesuchId");

            migrationBuilder.CreateIndex(
                name: "IX_KundenNotizen_KundeId_ErstelltUtc",
                table: "KundenNotizen",
                columns: new[] { "KundeId", "ErstelltUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KundenNotizen");

            migrationBuilder.DropTable(
                name: "KundenBesuche");
        }
    }
}
