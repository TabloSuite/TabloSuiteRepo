using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AllgemeinesBulletin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bestehende Vorlagen erhalten die üblichen Blöcke des allgemeinen Bulletins (Foto, Signaturspalte, Note in Worten bleiben aus).
            migrationBuilder.AddColumn<bool>(
                name: "IstAffecte",
                table: "StudentProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IstInterne",
                table: "StudentProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Nationalitaet",
                table: "StudentProfiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DokumentDirection",
                table: "Schools",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DokumentStatut",
                table: "Schools",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gruppe",
                table: "Faecher",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Reihenfolge",
                table: "Faecher",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeAppreciation",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeFachGruppen",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeFachRang",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeFoto",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeJahresBilanz",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeKlassenStatistik",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeMentionen",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeNoteInWorten",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigePunkte",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeSignaturSpalte",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeTeilnoten",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "KlassenratEntscheidung",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JahresZuordnungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Distinktion = table.Column<int>(type: "int", nullable: true),
                    Sanktionen = table.Column<int>(type: "int", nullable: false),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Entscheidung = table.Column<int>(type: "int", nullable: false),
                    BearbeitetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlassenratEntscheidung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KlassenratEntscheidung_BewertungsPeriode_PeriodeID",
                        column: x => x.PeriodeID,
                        principalTable: "BewertungsPeriode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KlassenratEntscheidung_SchuelerJahresZuordnung_JahresZuordnungID",
                        column: x => x.JahresZuordnungID,
                        principalTable: "SchuelerJahresZuordnung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEntscheidung_JahresZuordnungID",
                table: "KlassenratEntscheidung",
                column: "JahresZuordnungID");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEntscheidung_PeriodeID_JahresZuordnungID",
                table: "KlassenratEntscheidung",
                columns: new[] { "PeriodeID", "JahresZuordnungID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEntscheidung_SchulID",
                table: "KlassenratEntscheidung",
                column: "SchulID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KlassenratEntscheidung");

            migrationBuilder.DropColumn(
                name: "IstAffecte",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "IstInterne",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "Nationalitaet",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "DokumentDirection",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DokumentStatut",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Gruppe",
                table: "Faecher");

            migrationBuilder.DropColumn(
                name: "Reihenfolge",
                table: "Faecher");

            migrationBuilder.DropColumn(
                name: "ZeigeAppreciation",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeFachGruppen",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeFachRang",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeFoto",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeJahresBilanz",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeKlassenStatistik",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeMentionen",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeNoteInWorten",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigePunkte",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeSignaturSpalte",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeTeilnoten",
                table: "BulletinVorlage");
        }
    }
}
