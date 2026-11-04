using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class LeistungKategorieUndFavoriten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kategorie",
                table: "Leistungen");

            migrationBuilder.AddColumn<Guid>(
                name: "KategorieId",
                table: "Leistungen",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeistungFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeistungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GesetztVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeistungFavoriten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeistungFavoriten_Leistungen_LeistungId",
                        column: x => x.LeistungId,
                        principalTable: "Leistungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeistungKategorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeistungKategorien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeistungKategorien_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leistungen_KategorieId",
                table: "Leistungen",
                column: "KategorieId");

            migrationBuilder.CreateIndex(
                name: "IX_LeistungFavoriten_LeistungId",
                table: "LeistungFavoriten",
                column: "LeistungId");

            migrationBuilder.CreateIndex(
                name: "IX_LeistungFavoriten_SalonId_LeistungId",
                table: "LeistungFavoriten",
                columns: new[] { "SalonId", "LeistungId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeistungKategorien_SalonId_Name",
                table: "LeistungKategorien",
                columns: new[] { "SalonId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Leistungen_LeistungKategorien_KategorieId",
                table: "Leistungen",
                column: "KategorieId",
                principalTable: "LeistungKategorien",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leistungen_LeistungKategorien_KategorieId",
                table: "Leistungen");

            migrationBuilder.DropTable(
                name: "LeistungFavoriten");

            migrationBuilder.DropTable(
                name: "LeistungKategorien");

            migrationBuilder.DropIndex(
                name: "IX_Leistungen_KategorieId",
                table: "Leistungen");

            migrationBuilder.DropColumn(
                name: "KategorieId",
                table: "Leistungen");

            migrationBuilder.AddColumn<string>(
                name: "Kategorie",
                table: "Leistungen",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
