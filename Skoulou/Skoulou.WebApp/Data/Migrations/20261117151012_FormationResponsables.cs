using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoulou.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class FormationResponsables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormationLektionenGelesen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KontoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LektionId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    GelesenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormationLektionenGelesen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormationLektionenGelesen_Konten_KontoId",
                        column: x => x.KontoId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormationRegeln",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BestehensGrenzeProzent = table.Column<int>(type: "int", nullable: false),
                    FragenJePruefung = table.Column<int>(type: "int", nullable: false),
                    FristTage = table.Column<int>(type: "int", nullable: false),
                    StandardIntervallMonate = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormationRegeln", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormationRunden",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KontoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeoeffnetUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FristUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AbgeschlossenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EndNoteProzent = table.Column<int>(type: "int", nullable: true),
                    BestehensGrenzeProzent = table.Column<int>(type: "int", nullable: false),
                    FragenAnzahl = table.Column<int>(type: "int", nullable: false),
                    ErlassenGrund = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErlassenVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstFrei = table.Column<bool>(type: "bit", nullable: false),
                    BenachrichtigtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErinnerungUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AdminGemeldetUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormationRunden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormationRunden_Konten_KontoId",
                        column: x => x.KontoId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormationTeilnehmer",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KontoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntervallMonate = table.Column<int>(type: "int", nullable: false),
                    PausiertBisUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NaechsteRundeAbUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormationTeilnehmer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormationTeilnehmer_Konten_KontoId",
                        column: x => x.KontoId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormationVersuche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RundeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nummer = table.Column<int>(type: "int", nullable: false),
                    BegonnenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AbgegebenUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FragenJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AntwortenJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FragenAnzahl = table.Column<int>(type: "int", nullable: false),
                    Richtige = table.Column<int>(type: "int", nullable: true),
                    Prozent = table.Column<int>(type: "int", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormationVersuche", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormationVersuche_FormationRunden_RundeId",
                        column: x => x.RundeId,
                        principalTable: "FormationRunden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormationLektionenGelesen_KontoId_LektionId",
                table: "FormationLektionenGelesen",
                columns: new[] { "KontoId", "LektionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormationRunden_KontoId_Status",
                table: "FormationRunden",
                columns: new[] { "KontoId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FormationTeilnehmer_KontoId",
                table: "FormationTeilnehmer",
                column: "KontoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormationVersuche_RundeId_Nummer",
                table: "FormationVersuche",
                columns: new[] { "RundeId", "Nummer" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormationLektionenGelesen");

            migrationBuilder.DropTable(
                name: "FormationRegeln");

            migrationBuilder.DropTable(
                name: "FormationTeilnehmer");

            migrationBuilder.DropTable(
                name: "FormationVersuche");

            migrationBuilder.DropTable(
                name: "FormationRunden");
        }
    }
}
