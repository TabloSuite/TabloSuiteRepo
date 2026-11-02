using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class BulletinGestaltungUndAnwesenheit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockReihenfolge",
                table: "BulletinVorlage",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Schueler,Noten,Ergebnisse,Bemerkung,Unterschriften");

            migrationBuilder.AddColumn<Guid>(
                name: "LogoAssetID",
                table: "BulletinVorlage",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiegelAssetID",
                table: "BulletinVorlage",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuecknahmeGrund",
                table: "BulletinAusgabe",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ZurueckgezogenAmUtc",
                table: "BulletinAusgabe",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ZurueckgezogenVonID",
                table: "BulletinAusgabe",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AnwesenheitsEintrag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JahresZuordnungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IstEntschuldigt = table.Column<bool>(type: "bit", nullable: false),
                    VerspaetungsMinuten = table.Column<int>(type: "int", nullable: false),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BearbeitetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnwesenheitsEintrag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnwesenheitsEintrag_BewertungsPeriode_PeriodeID",
                        column: x => x.PeriodeID,
                        principalTable: "BewertungsPeriode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnwesenheitsEintrag_SchuelerJahresZuordnung_JahresZuordnungID",
                        column: x => x.JahresZuordnungID,
                        principalTable: "SchuelerJahresZuordnung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnwesenheitsEintrag_JahresZuordnungID",
                table: "AnwesenheitsEintrag",
                column: "JahresZuordnungID");

            migrationBuilder.CreateIndex(
                name: "IX_AnwesenheitsEintrag_PeriodeID_JahresZuordnungID_Datum",
                table: "AnwesenheitsEintrag",
                columns: new[] { "PeriodeID", "JahresZuordnungID", "Datum" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnwesenheitsEintrag");

            migrationBuilder.DropColumn(
                name: "BlockReihenfolge",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "LogoAssetID",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "SiegelAssetID",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "RuecknahmeGrund",
                table: "BulletinAusgabe");

            migrationBuilder.DropColumn(
                name: "ZurueckgezogenAmUtc",
                table: "BulletinAusgabe");

            migrationBuilder.DropColumn(
                name: "ZurueckgezogenVonID",
                table: "BulletinAusgabe");
        }
    }
}
