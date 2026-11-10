using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AnkuendigungenUndPreisAnpassung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreisAnpassungId",
                table: "MonatsPreisStaende",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Ankuendigungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Ziel = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkText = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SichtbarAbUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SichtbarBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IstAktiv = table.Column<bool>(type: "bit", nullable: false),
                    MailsGesendet = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ankuendigungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PreisAnpassungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlterStandard = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NeuerStandard = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GueltigAbUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AngepassteLaeden = table.Column<int>(type: "int", nullable: false),
                    MailsGesendet = table.Column<int>(type: "int", nullable: false),
                    AnkuendigungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstAufgehoben = table.Column<bool>(type: "bit", nullable: false),
                    IstAngewendet = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreisAnpassungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnkuendigungenGelesen",
                columns: table => new
                {
                    AnkuendigungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GelesenUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnkuendigungenGelesen", x => new { x.AnkuendigungId, x.UserId });
                    table.ForeignKey(
                        name: "FK_AnkuendigungenGelesen_Ankuendigungen_AnkuendigungId",
                        column: x => x.AnkuendigungId,
                        principalTable: "Ankuendigungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ankuendigungen_IstAktiv_SichtbarAbUtc",
                table: "Ankuendigungen",
                columns: new[] { "IstAktiv", "SichtbarAbUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PreisAnpassungen_GueltigAbUtc",
                table: "PreisAnpassungen",
                column: "GueltigAbUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnkuendigungenGelesen");

            migrationBuilder.DropTable(
                name: "PreisAnpassungen");

            migrationBuilder.DropTable(
                name: "Ankuendigungen");

            migrationBuilder.DropColumn(
                name: "PreisAnpassungId",
                table: "MonatsPreisStaende");
        }
    }
}
