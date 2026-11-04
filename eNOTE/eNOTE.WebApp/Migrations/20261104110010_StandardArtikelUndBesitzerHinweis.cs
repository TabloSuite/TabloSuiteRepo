using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class StandardArtikelUndBesitzerHinweis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IstStandardArtikel",
                table: "Leistungen",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BesitzerHinweise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AusgeloestVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AusgeloestVonName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IstUngelesen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GelesenUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BesitzerHinweise", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BesitzerHinweise_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BesitzerHinweise_SalonId_IstUngelesen",
                table: "BesitzerHinweise",
                columns: new[] { "SalonId", "IstUngelesen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BesitzerHinweise");

            migrationBuilder.DropColumn(
                name: "IstStandardArtikel",
                table: "Leistungen");
        }
    }
}
