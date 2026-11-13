using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoulou.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fundament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEintraege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AkteurId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Aktion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ZielTyp = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ZielId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEintraege", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FehlerProtokolle",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Stufe = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Nachricht = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Typ = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Pfad = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    KontoId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Bearbeitet = table.Column<bool>(type: "bit", nullable: false),
                    BearbeitetAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BearbeitetVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FehlerProtokolle", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MailAusgaenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    An = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AntwortAn = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Versuche = table.Column<int>(type: "int", nullable: false),
                    NaechsterVersuchUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Fehler = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Bezug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GesendetAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostmarkId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailAusgaenge", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlattformEinstellungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JahrespreisProSchueler = table.Column<long>(type: "bigint", nullable: false),
                    ZahlungszielTage = table.Column<int>(type: "int", nullable: false),
                    ResponsableProzentStandard = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    AgbVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlattformEinstellungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rollen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Beschreibung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rollen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RollenClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollenClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RollenClaims_Rollen_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Rollen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ankuendigungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InhaltHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Stufe = table.Column<int>(type: "int", nullable: false),
                    Zielrollen = table.Column<int>(type: "int", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ankuendigungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Antworten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersuchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AntwortJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Punkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Korrektur = table.Column<int>(type: "int", nullable: false),
                    KorrigiertVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KorrigiertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Feedback = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Antworten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Anwesenheiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Entschuldigt = table.Column<bool>(type: "bit", nullable: false),
                    Minuten = table.Column<int>(type: "int", nullable: true),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anwesenheiten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AufgabenBloecke",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeconId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PruefungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Consigne = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Archiviert = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AufgabenBloecke", x => x.Id);
                    table.CheckConstraint("CK_AufgabenBloecke_Inhalt", "([LeconId] IS NOT NULL AND [PruefungId] IS NULL) OR ([LeconId] IS NULL AND [PruefungId] IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "Bewertungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlassenfachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KomponenteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Titel = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: true),
                    Bareme = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    Gewicht = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bewertungen", x => x.Id);
                    table.CheckConstraint("CK_Bewertungen_Bareme", "[Bareme] > 0");
                    table.CheckConstraint("CK_Bewertungen_Gewicht", "[Gewicht] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Buchungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Richtung = table.Column<int>(type: "int", nullable: false),
                    KategorieId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Nummer = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Herkunft = table.Column<int>(type: "int", nullable: false),
                    HerkunftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BelegMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StornoVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buchungen", x => x.Id);
                    table.CheckConstraint("CK_Buchungen_Betrag", "[Betrag] > 0");
                    table.ForeignKey(
                        name: "FK_Buchungen_Buchungen_StornoVonId",
                        column: x => x.StornoVonId,
                        principalTable: "Buchungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bulletins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nummer = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PruefToken = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErsetztId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IstRectificatif = table.Column<bool>(type: "bit", nullable: false),
                    Grund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LogoMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FotoMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VeroeffentlichtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bulletins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bulletins_Bulletins_ErsetztId",
                        column: x => x.ErsetztId,
                        principalTable: "Bulletins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BulletinUebergaben",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BulletinId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Vermerk = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    VonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinUebergaben", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulletinUebergaben_Bulletins_BulletinId",
                        column: x => x.BulletinId,
                        principalTable: "Bulletins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BulletinVorlagen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Farbe = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    ZeigeKoeffizienten = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeRang = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeVorperioden = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeUnterschriften = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeLehrkraefte = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeAnwesenheit = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeTeilnoten = table.Column<bool>(type: "bit", nullable: false),
                    ZeigePunkte = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeFachRang = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeAppreciation = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeSignaturSpalte = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeFachGruppen = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeKlassenStatistik = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeMentionen = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeJahresBilanz = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeFoto = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeNoteInWorten = table.Column<bool>(type: "bit", nullable: false),
                    SchriftGroesse = table.Column<int>(type: "int", nullable: false),
                    SeitenRand = table.Column<int>(type: "int", nullable: false),
                    Fusstext = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinVorlagen", x => x.Id);
                    table.CheckConstraint("CK_BulletinVorlagen_Rand", "[SeitenRand] BETWEEN 15 AND 50");
                    table.CheckConstraint("CK_BulletinVorlagen_Schrift", "[SchriftGroesse] BETWEEN 8 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "Einschreibungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Von = table.Column<DateOnly>(type: "date", nullable: false),
                    Bis = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IstWiederholer = table.Column<bool>(type: "bit", nullable: false),
                    Austrittsgrund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StorniertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StornoGrund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Einschreibungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElternKinder",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ElternId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Beziehung = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElternKinder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ermaessigungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Wert = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Grund = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ermaessigungen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FachBemerkungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlassenfachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FachBemerkungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FachBemerkungen_Einschreibungen_EinschreibungId",
                        column: x => x.EinschreibungId,
                        principalTable: "Einschreibungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Faecher",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Kuerzel = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    Gruppe = table.Column<int>(type: "int", nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faecher", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormelFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KontoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaTeX = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormelFavoriten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Fragen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Typ = table.Column<int>(type: "int", nullable: false),
                    TextHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Punkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    AudioText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Sprache = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AkzeptierteAntworten = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LueckenJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Wortbank = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AkzenteBeachten = table.Column<bool>(type: "bit", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    VorgaengerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Archiviert = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fragen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fragen_AufgabenBloecke_BlockId",
                        column: x => x.BlockId,
                        principalTable: "AufgabenBloecke",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fragen_Fragen_VorgaengerId",
                        column: x => x.VorgaengerId,
                        principalTable: "Fragen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FrageOptionen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FrageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Richtig = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrageOptionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FrageOptionen_Fragen_FrageId",
                        column: x => x.FrageId,
                        principalTable: "Fragen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GebuehrenPosten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    StufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Pflicht = table.Column<bool>(type: "bit", nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GebuehrenPosten", x => x.Id);
                    table.CheckConstraint("CK_GebuehrenPosten_Betrag", "[Betrag] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "GebuehrenRaten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    Faellig = table.Column<DateOnly>(type: "date", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GebuehrenRaten", x => x.Id);
                    table.CheckConstraint("CK_GebuehrenRaten_Betrag", "[Betrag] >= 0");
                    table.ForeignKey(
                        name: "FK_GebuehrenRaten_GebuehrenPosten_PostenId",
                        column: x => x.PostenId,
                        principalTable: "GebuehrenPosten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Jahresentscheidungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Entscheidung = table.Column<int>(type: "int", nullable: false),
                    ZielStufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AusgefuehrtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jahresentscheidungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jahresentscheidungen_Einschreibungen_EinschreibungId",
                        column: x => x.EinschreibungId,
                        principalTable: "Einschreibungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Kategorien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Richtung = table.Column<int>(type: "int", nullable: false),
                    SystemCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kategorien", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Klassen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kapazitaet = table.Column<int>(type: "int", nullable: true),
                    KlassenlehrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Raum = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Klassen", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Klassenfaecher",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Koeffizient = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Klassenfaecher", x => x.Id);
                    table.CheckConstraint("CK_Klassenfaecher_Koeffizient", "[Koeffizient] > 0");
                    table.ForeignKey(
                        name: "FK_Klassenfaecher_Faecher_FachId",
                        column: x => x.FachId,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Klassenfaecher_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KlassenratEintraege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Distinktion = table.Column<int>(type: "int", nullable: true),
                    Sanktionen = table.Column<int>(type: "int", nullable: false),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlassenratEintraege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KlassenratEintraege_Einschreibungen_EinschreibungId",
                        column: x => x.EinschreibungId,
                        principalTable: "Einschreibungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KlassenVeroeffentlichungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    VeroeffentlichtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VeroeffentlichtVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RectificationGrund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlassenVeroeffentlichungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KlassenVeroeffentlichungen_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Konten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Vorname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Nachname = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Telefon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Geschlecht = table.Column<int>(type: "int", nullable: false),
                    Geburtsdatum = table.Column<DateOnly>(type: "date", nullable: true),
                    Rolle = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KoordinatorRechte = table.Column<int>(type: "int", nullable: false),
                    MussPasswortAendern = table.Column<bool>(type: "bit", nullable: false),
                    AktiveSitzungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FotoMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AgbVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AgbAkzeptiertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LetzteAnmeldungUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SelbstRegistriert = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Konten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KontoClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KontoClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KontoClaims_Konten_UserId",
                        column: x => x.UserId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KontoLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KontoLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_KontoLogins_Konten_UserId",
                        column: x => x.UserId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KontoRollen",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KontoRollen", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_KontoRollen_Konten_UserId",
                        column: x => x.UserId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KontoRollen_Rollen_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Rollen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KontoTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KontoTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_KontoTokens_Konten_UserId",
                        column: x => x.UserId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KorrekturEintraege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AntwortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersuchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AltePunkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    NeuePunkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    Feedback = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    KorrigiertVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KorrekturEintraege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KorrekturEintraege_Antworten_AntwortId",
                        column: x => x.AntwortId,
                        principalTable: "Antworten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KorrekturEintraege_Konten_KorrigiertVonId",
                        column: x => x.KorrigiertVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Lecons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Zusammenfassung = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Thema = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VideoMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lecons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lecons_Faecher_FachId",
                        column: x => x.FachId,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lecons_Konten_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lecons_Konten_ErfasstVonId",
                        column: x => x.ErfasstVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeconSeiten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeconId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeconSeiten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeconSeiten_Lecons_LeconId",
                        column: x => x.LeconId,
                        principalTable: "Lecons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lehrauftraege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlassenfachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Wochenstunden = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lehrauftraege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lehrauftraege_Klassenfaecher_KlassenfachId",
                        column: x => x.KlassenfachId,
                        principalTable: "Klassenfaecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Lehrauftraege_Konten_LehrerId",
                        column: x => x.LehrerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LernStaende",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZuweisungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BestePunkte = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    MaxPunkte = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Prozent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Abgeschlossen = table.Column<bool>(type: "bit", nullable: false),
                    AnzahlVersuche = table.Column<int>(type: "int", nullable: false),
                    KorrekturOffen = table.Column<bool>(type: "bit", nullable: false),
                    LetzteAktivitaetUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LernStaende", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LernStaende_Konten_SchuelerId",
                        column: x => x.SchuelerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Medien",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BesitzerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Zweck = table.Column<int>(type: "int", nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Dateiname = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Bytes = table.Column<long>(type: "bigint", nullable: false),
                    DauerSek = table.Column<int>(type: "int", nullable: true),
                    Breite = table.Column<int>(type: "int", nullable: true),
                    Hoehe = table.Column<int>(type: "int", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medien", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Medien_Konten_BesitzerId",
                        column: x => x.BesitzerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedienNutzungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Typ = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ZielId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedienNutzungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedienNutzungen_Medien_MediumId",
                        column: x => x.MediumId,
                        principalTable: "Medien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Schulen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameNormalisiert = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Ort = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Land = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusGrund = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Zeitzone = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JahrespreisOverride = table.Column<long>(type: "bigint", nullable: true),
                    ResponsableProzent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    OffizielleBezeichnung = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Slogan = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Adresse = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Telefon = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Zulassungsnummer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Ministerium = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RepublikZeile = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StaatsMotto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DirectionRegionale = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Statut = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    LogoMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MatrikelPraefix = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    AbsenzOhneEntschuldigungZaehltNull = table.Column<bool>(type: "bit", nullable: false),
                    ElternSehenNotenLive = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schulen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schulen_Konten_ResponsableId",
                        column: x => x.ResponsableId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Schulen_Medien_LogoMediumId",
                        column: x => x.LogoMediumId,
                        principalTable: "Medien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Nachrichten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Richtung = table.Column<int>(type: "int", nullable: false),
                    Von = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    An = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TextHtml = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GelesenAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AntwortAufId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GesendetVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostmarkId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nachrichten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nachrichten_Konten_GesendetVonId",
                        column: x => x.GesendetVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Nachrichten_Nachrichten_AntwortAufId",
                        column: x => x.AntwortAufId,
                        principalTable: "Nachrichten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Nachrichten_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Noten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BewertungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Punkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Noten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Noten_Bewertungen_BewertungId",
                        column: x => x.BewertungId,
                        principalTable: "Bewertungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Noten_Einschreibungen_EinschreibungId",
                        column: x => x.EinschreibungId,
                        principalTable: "Einschreibungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Noten_Konten_ErfasstVonId",
                        column: x => x.ErfasstVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Noten_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Nummernkreise",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Jahr = table.Column<int>(type: "int", nullable: false),
                    Letzte = table.Column<long>(type: "bigint", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nummernkreise", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nummernkreise_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchuelerProfile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KontoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Matrikel = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Geburtsort = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Nationalitaet = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IstInterne = table.Column<bool>(type: "bit", nullable: false),
                    IstAffecte = table.Column<bool>(type: "bit", nullable: false),
                    ElternTelefon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ElternEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StimmeAus = table.Column<bool>(type: "bit", nullable: false),
                    AufnahmeBestaetigtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KartenToken = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchuelerProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchuelerProfile_Konten_KontoId",
                        column: x => x.KontoId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchuelerProfile_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Schuljahre",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Beginn = table.Column<DateOnly>(type: "date", nullable: false),
                    Ende = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PeriodenArt = table.Column<int>(type: "int", nullable: false),
                    PlattformJahrespreis = table.Column<long>(type: "bigint", nullable: true),
                    AbgeschlossenAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WiedereroeffnetGrund = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schuljahre", x => x.Id);
                    table.CheckConstraint("CK_Schuljahre_Zeitraum", "[Ende] > [Beginn]");
                    table.ForeignKey(
                        name: "FK_Schuljahre_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stufen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false),
                    NachfolgerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Zyklus = table.Column<int>(type: "int", nullable: true),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stufen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stufen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stufen_Stufen_NachfolgerId",
                        column: x => x.NachfolgerId,
                        principalTable: "Stufen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stundenplaene",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GueltigAb = table.Column<DateOnly>(type: "date", nullable: false),
                    GueltigBis = table.Column<DateOnly>(type: "date", nullable: true),
                    Veroeffentlicht = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stundenplaene", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stundenplaene_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stundenplaene_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotenAenderungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AltePunkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    AlterStatus = table.Column<int>(type: "int", nullable: true),
                    NeuePunkte = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    NeuerStatus = table.Column<int>(type: "int", nullable: false),
                    GeaendertVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZeitUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Grund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotenAenderungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotenAenderungen_Konten_GeaendertVonId",
                        column: x => x.GeaendertVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotenAenderungen_Noten_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Noten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotenAenderungen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Perioden",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Beginn = table.Column<DateOnly>(type: "date", nullable: false),
                    Ende = table.Column<DateOnly>(type: "date", nullable: false),
                    Jahresgewicht = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ZaehltFuerJahr = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perioden", x => x.Id);
                    table.CheckConstraint("CK_Perioden_Zeitraum", "[Ende] >= [Beginn]");
                    table.ForeignKey(
                        name: "FK_Perioden_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Perioden_Schuljahre_SchuljahrId",
                        column: x => x.SchuljahrId,
                        principalTable: "Schuljahre",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Zahlungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Kanal = table.Column<int>(type: "int", nullable: false),
                    Referenz = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quittungsnummer = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorniertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StorniertVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StornoGrund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zahlungen", x => x.Id);
                    table.CheckConstraint("CK_Zahlungen_Betrag", "[Betrag] > 0");
                    table.ForeignKey(
                        name: "FK_Zahlungen_Konten_ErfasstVonId",
                        column: x => x.ErfasstVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungen_Konten_SchuelerId",
                        column: x => x.SchuelerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungen_Konten_StorniertVonId",
                        column: x => x.StorniertVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungen_Schuljahre_SchuljahrId",
                        column: x => x.SchuljahrId,
                        principalTable: "Schuljahre",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pruefungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StufeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Beschreibung = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DauerMinuten = table.Column<int>(type: "int", nullable: false),
                    Bestehensgrenze = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pruefungen", x => x.Id);
                    table.CheckConstraint("CK_Pruefungen_Dauer", "[DauerMinuten] > 0");
                    table.ForeignKey(
                        name: "FK_Pruefungen_Faecher_FachId",
                        column: x => x.FachId,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pruefungen_Konten_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pruefungen_Konten_ErfasstVonId",
                        column: x => x.ErfasstVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pruefungen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Pruefungen_Stufen_StufeId",
                        column: x => x.StufeId,
                        principalTable: "Stufen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StundenplanEintraege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StundenplanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrauftragId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Wochentag = table.Column<int>(type: "int", nullable: false),
                    Beginn = table.Column<TimeOnly>(type: "time", nullable: false),
                    Ende = table.Column<TimeOnly>(type: "time", nullable: false),
                    Raum = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StundenplanEintraege", x => x.Id);
                    table.CheckConstraint("CK_StundenplanEintraege_Zeit", "[Ende] > [Beginn]");
                    table.ForeignKey(
                        name: "FK_StundenplanEintraege_Lehrauftraege_LehrauftragId",
                        column: x => x.LehrauftragId,
                        principalTable: "Lehrauftraege",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StundenplanEintraege_Stundenplaene_StundenplanId",
                        column: x => x.StundenplanId,
                        principalTable: "Stundenplaene",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PeriodenKomponenten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Gewicht = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Bareme = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodenKomponenten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodenKomponenten_Perioden_PeriodeId",
                        column: x => x.PeriodeId,
                        principalTable: "Perioden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PeriodenKomponenten_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Plattformrechnungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nummer = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Faellig = table.Column<DateOnly>(type: "date", nullable: false),
                    Anzahl = table.Column<int>(type: "int", nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Grund = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErgaenztId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BestaetigtVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BestaetigtAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plattformrechnungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plattformrechnungen_Konten_BestaetigtVonId",
                        column: x => x.BestaetigtVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Plattformrechnungen_Perioden_PeriodeId",
                        column: x => x.PeriodeId,
                        principalTable: "Perioden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Plattformrechnungen_Plattformrechnungen_ErgaenztId",
                        column: x => x.ErgaenztId,
                        principalTable: "Plattformrechnungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Plattformrechnungen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Plattformrechnungen_Schuljahre_SchuljahrId",
                        column: x => x.SchuljahrId,
                        principalTable: "Schuljahre",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Zuweisungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeconId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PruefungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    KlasseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AbUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FristUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxVersuche = table.Column<int>(type: "int", nullable: true),
                    Wertung = table.Column<int>(type: "int", nullable: false),
                    LoesungenSichtbar = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zuweisungen", x => x.Id);
                    table.CheckConstraint("CK_Zuweisungen_Inhalt", "([LeconId] IS NOT NULL AND [PruefungId] IS NULL) OR ([LeconId] IS NULL AND [PruefungId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Zuweisungen_Klassen_KlasseId",
                        column: x => x.KlasseId,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zuweisungen_Konten_LehrerId",
                        column: x => x.LehrerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zuweisungen_Lecons_LeconId",
                        column: x => x.LeconId,
                        principalTable: "Lecons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zuweisungen_Pruefungen_PruefungId",
                        column: x => x.PruefungId,
                        principalTable: "Pruefungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zuweisungen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Rechnungspositionen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RechnungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EinschreibungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    StorniertGrund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    StorniertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rechnungspositionen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rechnungspositionen_Einschreibungen_EinschreibungId",
                        column: x => x.EinschreibungId,
                        principalTable: "Einschreibungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rechnungspositionen_Konten_SchuelerId",
                        column: x => x.SchuelerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rechnungspositionen_Perioden_PeriodeId",
                        column: x => x.PeriodeId,
                        principalTable: "Perioden",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Rechnungspositionen_Plattformrechnungen_RechnungId",
                        column: x => x.RechnungId,
                        principalTable: "Plattformrechnungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResponsableAnteile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponsableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RechnungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Prozent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    AusgleichFuerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AusgezahltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuszahlungsReferenz = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AusgezahltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponsableAnteile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResponsableAnteile_Konten_AusgezahltVonId",
                        column: x => x.AusgezahltVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResponsableAnteile_Konten_ResponsableId",
                        column: x => x.ResponsableId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResponsableAnteile_Plattformrechnungen_RechnungId",
                        column: x => x.RechnungId,
                        principalTable: "Plattformrechnungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResponsableAnteile_ResponsableAnteile_AusgleichFuerId",
                        column: x => x.AusgleichFuerId,
                        principalTable: "ResponsableAnteile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResponsableAnteile_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Zahlungsmeldungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RechnungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Betrag = table.Column<long>(type: "bigint", nullable: false),
                    Datum = table.Column<DateOnly>(type: "date", nullable: false),
                    Referenz = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BelegMediumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GemeldetVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuchungId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zahlungsmeldungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zahlungsmeldungen_Buchungen_BuchungId",
                        column: x => x.BuchungId,
                        principalTable: "Buchungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungsmeldungen_Konten_GemeldetVonId",
                        column: x => x.GemeldetVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungsmeldungen_Medien_BelegMediumId",
                        column: x => x.BelegMediumId,
                        principalTable: "Medien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Zahlungsmeldungen_Plattformrechnungen_RechnungId",
                        column: x => x.RechnungId,
                        principalTable: "Plattformrechnungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Versuche",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZuweisungId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Nr = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AbgabeUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeadlineUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Punkte = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    MaxPunkte = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FragenIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Versuche", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Versuche_Konten_SchuelerId",
                        column: x => x.SchuelerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Versuche_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Versuche_Zuweisungen_ZuweisungId",
                        column: x => x.ZuweisungId,
                        principalTable: "Zuweisungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ankuendigungen_ErstelltVonId",
                table: "Ankuendigungen",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Ankuendigungen_SchuleId",
                table: "Ankuendigungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Ankuendigungen_StartUtc_EndeUtc",
                table: "Ankuendigungen",
                columns: new[] { "StartUtc", "EndeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Antworten_FrageId",
                table: "Antworten",
                column: "FrageId");

            migrationBuilder.CreateIndex(
                name: "IX_Antworten_KorrigiertVonId",
                table: "Antworten",
                column: "KorrigiertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Antworten_VersuchId_FrageId",
                table: "Antworten",
                columns: new[] { "VersuchId", "FrageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Anwesenheiten_EinschreibungId_Datum",
                table: "Anwesenheiten",
                columns: new[] { "EinschreibungId", "Datum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Anwesenheiten_ErfasstVonId",
                table: "Anwesenheiten",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Anwesenheiten_SchuleId_Datum",
                table: "Anwesenheiten",
                columns: new[] { "SchuleId", "Datum" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEintraege_SchuleId_ZeitUtc",
                table: "AuditEintraege",
                columns: new[] { "SchuleId", "ZeitUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEintraege_ZeitUtc",
                table: "AuditEintraege",
                column: "ZeitUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEintraege_ZielTyp_ZielId",
                table: "AuditEintraege",
                columns: new[] { "ZielTyp", "ZielId" });

            migrationBuilder.CreateIndex(
                name: "IX_AufgabenBloecke_LeconId_Nr",
                table: "AufgabenBloecke",
                columns: new[] { "LeconId", "Nr" },
                unique: true,
                filter: "[LeconId] IS NOT NULL AND [Archiviert] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AufgabenBloecke_PruefungId_Nr",
                table: "AufgabenBloecke",
                columns: new[] { "PruefungId", "Nr" },
                unique: true,
                filter: "[PruefungId] IS NOT NULL AND [Archiviert] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AufgabenBloecke_SchuleId",
                table: "AufgabenBloecke",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Bewertungen_ErstelltVonId",
                table: "Bewertungen",
                column: "ErstelltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Bewertungen_KlassenfachId_PeriodeId",
                table: "Bewertungen",
                columns: new[] { "KlassenfachId", "PeriodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Bewertungen_KomponenteId",
                table: "Bewertungen",
                column: "KomponenteId");

            migrationBuilder.CreateIndex(
                name: "IX_Bewertungen_PeriodeId",
                table: "Bewertungen",
                column: "PeriodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Bewertungen_SchuleId",
                table: "Bewertungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_BelegMediumId",
                table: "Buchungen",
                column: "BelegMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_ErfasstVonId",
                table: "Buchungen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_Herkunft_HerkunftId",
                table: "Buchungen",
                columns: new[] { "Herkunft", "HerkunftId" });

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_KategorieId",
                table: "Buchungen",
                column: "KategorieId");

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_SchuleId_Datum",
                table: "Buchungen",
                columns: new[] { "SchuleId", "Datum" });

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_SchuleId_Nummer",
                table: "Buchungen",
                columns: new[] { "SchuleId", "Nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buchungen_StornoVonId",
                table: "Buchungen",
                column: "StornoVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_EinGueltiges",
                table: "Bulletins",
                columns: new[] { "PeriodeId", "EinschreibungId" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_EinschreibungId",
                table: "Bulletins",
                column: "EinschreibungId");

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_ErsetztId",
                table: "Bulletins",
                column: "ErsetztId");

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_FotoMediumId",
                table: "Bulletins",
                column: "FotoMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_LogoMediumId",
                table: "Bulletins",
                column: "LogoMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_PruefToken",
                table: "Bulletins",
                column: "PruefToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bulletins_SchuleId_Nummer",
                table: "Bulletins",
                columns: new[] { "SchuleId", "Nummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BulletinUebergaben_BulletinId",
                table: "BulletinUebergaben",
                column: "BulletinId");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinUebergaben_SchuleId",
                table: "BulletinUebergaben",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinUebergaben_VonId",
                table: "BulletinUebergaben",
                column: "VonId");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinVorlagen_SchuleId_StufeId",
                table: "BulletinVorlagen",
                columns: new[] { "SchuleId", "StufeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BulletinVorlagen_StufeId",
                table: "BulletinVorlagen",
                column: "StufeId");

            migrationBuilder.CreateIndex(
                name: "IX_Einschreibungen_EineAktive",
                table: "Einschreibungen",
                column: "SchuelerId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Einschreibungen_KlasseId_Status",
                table: "Einschreibungen",
                columns: new[] { "KlasseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Einschreibungen_SchuleId",
                table: "Einschreibungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Einschreibungen_SchuljahrId_SchuelerId",
                table: "Einschreibungen",
                columns: new[] { "SchuljahrId", "SchuelerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElternKinder_ElternId_SchuelerId",
                table: "ElternKinder",
                columns: new[] { "ElternId", "SchuelerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElternKinder_SchuelerId",
                table: "ElternKinder",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_ElternKinder_SchuleId",
                table: "ElternKinder",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Ermaessigungen_ErfasstVonId",
                table: "Ermaessigungen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Ermaessigungen_PostenId",
                table: "Ermaessigungen",
                column: "PostenId");

            migrationBuilder.CreateIndex(
                name: "IX_Ermaessigungen_SchuelerId",
                table: "Ermaessigungen",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_Ermaessigungen_SchuleId",
                table: "Ermaessigungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Ermaessigungen_SchuljahrId_SchuelerId",
                table: "Ermaessigungen",
                columns: new[] { "SchuljahrId", "SchuelerId" });

            migrationBuilder.CreateIndex(
                name: "IX_FachBemerkungen_EinschreibungId",
                table: "FachBemerkungen",
                column: "EinschreibungId");

            migrationBuilder.CreateIndex(
                name: "IX_FachBemerkungen_ErfasstVonId",
                table: "FachBemerkungen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_FachBemerkungen_KlassenfachId_PeriodeId_EinschreibungId",
                table: "FachBemerkungen",
                columns: new[] { "KlassenfachId", "PeriodeId", "EinschreibungId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FachBemerkungen_PeriodeId",
                table: "FachBemerkungen",
                column: "PeriodeId");

            migrationBuilder.CreateIndex(
                name: "IX_FachBemerkungen_SchuleId",
                table: "FachBemerkungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Faecher_SchuleId_Name",
                table: "Faecher",
                columns: new[] { "SchuleId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FehlerProtokolle_Bearbeitet_ZeitUtc",
                table: "FehlerProtokolle",
                columns: new[] { "Bearbeitet", "ZeitUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FehlerProtokolle_ZeitUtc",
                table: "FehlerProtokolle",
                column: "ZeitUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FormelFavoriten_KontoId",
                table: "FormelFavoriten",
                column: "KontoId");

            migrationBuilder.CreateIndex(
                name: "IX_Fragen_BlockId_Nr",
                table: "Fragen",
                columns: new[] { "BlockId", "Nr" },
                unique: true,
                filter: "[Archiviert] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Fragen_SchuleId",
                table: "Fragen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Fragen_VorgaengerId",
                table: "Fragen",
                column: "VorgaengerId");

            migrationBuilder.CreateIndex(
                name: "IX_FrageOptionen_FrageId_Nr",
                table: "FrageOptionen",
                columns: new[] { "FrageId", "Nr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GebuehrenPosten_KlasseId",
                table: "GebuehrenPosten",
                column: "KlasseId");

            migrationBuilder.CreateIndex(
                name: "IX_GebuehrenPosten_SchuleId",
                table: "GebuehrenPosten",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_GebuehrenPosten_SchuljahrId_Reihenfolge",
                table: "GebuehrenPosten",
                columns: new[] { "SchuljahrId", "Reihenfolge" });

            migrationBuilder.CreateIndex(
                name: "IX_GebuehrenPosten_StufeId",
                table: "GebuehrenPosten",
                column: "StufeId");

            migrationBuilder.CreateIndex(
                name: "IX_GebuehrenRaten_PostenId_Nr",
                table: "GebuehrenRaten",
                columns: new[] { "PostenId", "Nr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jahresentscheidungen_EinschreibungId",
                table: "Jahresentscheidungen",
                column: "EinschreibungId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jahresentscheidungen_SchuleId",
                table: "Jahresentscheidungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Jahresentscheidungen_ZielStufeId",
                table: "Jahresentscheidungen",
                column: "ZielStufeId");

            migrationBuilder.CreateIndex(
                name: "IX_Kategorien_SchuleId_Richtung_Name",
                table: "Kategorien",
                columns: new[] { "SchuleId", "Richtung", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kategorien_SystemCode",
                table: "Kategorien",
                column: "SystemCode");

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_KlassenlehrerId",
                table: "Klassen",
                column: "KlassenlehrerId");

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_PeriodeId",
                table: "Klassen",
                column: "PeriodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_SchuleId",
                table: "Klassen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_SchuljahrId_Name",
                table: "Klassen",
                columns: new[] { "SchuljahrId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Klassen_StufeId",
                table: "Klassen",
                column: "StufeId");

            migrationBuilder.CreateIndex(
                name: "IX_Klassenfaecher_FachId",
                table: "Klassenfaecher",
                column: "FachId");

            migrationBuilder.CreateIndex(
                name: "IX_Klassenfaecher_KlasseId_FachId",
                table: "Klassenfaecher",
                columns: new[] { "KlasseId", "FachId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Klassenfaecher_SchuleId",
                table: "Klassenfaecher",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEintraege_EinschreibungId",
                table: "KlassenratEintraege",
                column: "EinschreibungId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEintraege_ErfasstVonId",
                table: "KlassenratEintraege",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEintraege_PeriodeId_EinschreibungId",
                table: "KlassenratEintraege",
                columns: new[] { "PeriodeId", "EinschreibungId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KlassenratEintraege_SchuleId",
                table: "KlassenratEintraege",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenVeroeffentlichungen_KlasseId_PeriodeId",
                table: "KlassenVeroeffentlichungen",
                columns: new[] { "KlasseId", "PeriodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KlassenVeroeffentlichungen_PeriodeId",
                table: "KlassenVeroeffentlichungen",
                column: "PeriodeId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenVeroeffentlichungen_SchuleId",
                table: "KlassenVeroeffentlichungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_KlassenVeroeffentlichungen_VeroeffentlichtVonId",
                table: "KlassenVeroeffentlichungen",
                column: "VeroeffentlichtVonId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Konten",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Konten_FotoMediumId",
                table: "Konten",
                column: "FotoMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_Konten_Rolle",
                table: "Konten",
                column: "Rolle");

            migrationBuilder.CreateIndex(
                name: "IX_Konten_SchuleId_Rolle_Status",
                table: "Konten",
                columns: new[] { "SchuleId", "Rolle", "Status" });

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Konten",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KontoClaims_UserId",
                table: "KontoClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KontoLogins_UserId",
                table: "KontoLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_KontoRollen_RoleId",
                table: "KontoRollen",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_KorrekturEintraege_AntwortId",
                table: "KorrekturEintraege",
                column: "AntwortId");

            migrationBuilder.CreateIndex(
                name: "IX_KorrekturEintraege_KorrigiertVonId",
                table: "KorrekturEintraege",
                column: "KorrigiertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_KorrekturEintraege_SchuleId",
                table: "KorrekturEintraege",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_KorrekturEintraege_VersuchId",
                table: "KorrekturEintraege",
                column: "VersuchId");

            migrationBuilder.CreateIndex(
                name: "IX_Lecons_AutorId",
                table: "Lecons",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_Lecons_ErfasstVonId",
                table: "Lecons",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Lecons_FachId",
                table: "Lecons",
                column: "FachId");

            migrationBuilder.CreateIndex(
                name: "IX_Lecons_SchuleId_StufeId_FachId",
                table: "Lecons",
                columns: new[] { "SchuleId", "StufeId", "FachId" });

            migrationBuilder.CreateIndex(
                name: "IX_Lecons_StufeId",
                table: "Lecons",
                column: "StufeId");

            migrationBuilder.CreateIndex(
                name: "IX_Lecons_VideoMediumId",
                table: "Lecons",
                column: "VideoMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_LeconSeiten_LeconId_Nr",
                table: "LeconSeiten",
                columns: new[] { "LeconId", "Nr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lehrauftraege_KlassenfachId_LehrerId",
                table: "Lehrauftraege",
                columns: new[] { "KlassenfachId", "LehrerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lehrauftraege_LehrerId",
                table: "Lehrauftraege",
                column: "LehrerId");

            migrationBuilder.CreateIndex(
                name: "IX_Lehrauftraege_SchuleId",
                table: "Lehrauftraege",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_LernStaende_SchuelerId",
                table: "LernStaende",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_LernStaende_SchuleId",
                table: "LernStaende",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_LernStaende_ZuweisungId_SchuelerId",
                table: "LernStaende",
                columns: new[] { "ZuweisungId", "SchuelerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MailAusgaenge_ErstelltAmUtc",
                table: "MailAusgaenge",
                column: "ErstelltAmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MailAusgaenge_Status_NaechsterVersuchUtc",
                table: "MailAusgaenge",
                columns: new[] { "Status", "NaechsterVersuchUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Medien_BesitzerId",
                table: "Medien",
                column: "BesitzerId");

            migrationBuilder.CreateIndex(
                name: "IX_Medien_BlobName",
                table: "Medien",
                column: "BlobName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Medien_SchuleId_ErstelltAmUtc",
                table: "Medien",
                columns: new[] { "SchuleId", "ErstelltAmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MedienNutzungen_MediumId_Typ_ZielId",
                table: "MedienNutzungen",
                columns: new[] { "MediumId", "Typ", "ZielId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedienNutzungen_Typ_ZielId",
                table: "MedienNutzungen",
                columns: new[] { "Typ", "ZielId" });

            migrationBuilder.CreateIndex(
                name: "IX_Nachrichten_AntwortAufId",
                table: "Nachrichten",
                column: "AntwortAufId");

            migrationBuilder.CreateIndex(
                name: "IX_Nachrichten_GesendetVonId",
                table: "Nachrichten",
                column: "GesendetVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Nachrichten_PostmarkId",
                table: "Nachrichten",
                column: "PostmarkId",
                unique: true,
                filter: "[PostmarkId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Nachrichten_Richtung_ErstelltAmUtc",
                table: "Nachrichten",
                columns: new[] { "Richtung", "ErstelltAmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Nachrichten_SchuleId_ErstelltAmUtc",
                table: "Nachrichten",
                columns: new[] { "SchuleId", "ErstelltAmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Noten_BewertungId_EinschreibungId",
                table: "Noten",
                columns: new[] { "BewertungId", "EinschreibungId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Noten_EinschreibungId",
                table: "Noten",
                column: "EinschreibungId");

            migrationBuilder.CreateIndex(
                name: "IX_Noten_ErfasstVonId",
                table: "Noten",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Noten_SchuleId",
                table: "Noten",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_NotenAenderungen_GeaendertVonId",
                table: "NotenAenderungen",
                column: "GeaendertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_NotenAenderungen_NoteId_ZeitUtc",
                table: "NotenAenderungen",
                columns: new[] { "NoteId", "ZeitUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotenAenderungen_SchuleId",
                table: "NotenAenderungen",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Nummernkreise_SchuleId_Art_Jahr",
                table: "Nummernkreise",
                columns: new[] { "SchuleId", "Art", "Jahr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Perioden_SchuleId",
                table: "Perioden",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Perioden_SchuljahrId_Nr",
                table: "Perioden",
                columns: new[] { "SchuljahrId", "Nr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodenKomponenten_PeriodeId_Titel",
                table: "PeriodenKomponenten",
                columns: new[] { "PeriodeId", "Titel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodenKomponenten_SchuleId",
                table: "PeriodenKomponenten",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_BestaetigtVonId",
                table: "Plattformrechnungen",
                column: "BestaetigtVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_ErgaenztId",
                table: "Plattformrechnungen",
                column: "ErgaenztId");

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_Nummer",
                table: "Plattformrechnungen",
                column: "Nummer",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_PeriodeId",
                table: "Plattformrechnungen",
                column: "PeriodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_SchuleId_PeriodeId",
                table: "Plattformrechnungen",
                columns: new[] { "SchuleId", "PeriodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_SchuljahrId",
                table: "Plattformrechnungen",
                column: "SchuljahrId");

            migrationBuilder.CreateIndex(
                name: "IX_Plattformrechnungen_Status_Faellig",
                table: "Plattformrechnungen",
                columns: new[] { "Status", "Faellig" });

            migrationBuilder.CreateIndex(
                name: "IX_Pruefungen_AutorId",
                table: "Pruefungen",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_Pruefungen_ErfasstVonId",
                table: "Pruefungen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Pruefungen_FachId",
                table: "Pruefungen",
                column: "FachId");

            migrationBuilder.CreateIndex(
                name: "IX_Pruefungen_SchuleId_StufeId_FachId",
                table: "Pruefungen",
                columns: new[] { "SchuleId", "StufeId", "FachId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pruefungen_StufeId",
                table: "Pruefungen",
                column: "StufeId");

            migrationBuilder.CreateIndex(
                name: "IX_Rechnungspositionen_EinmalJePeriode",
                table: "Rechnungspositionen",
                columns: new[] { "PeriodeId", "SchuelerId" },
                unique: true,
                filter: "[StorniertGrund] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Rechnungspositionen_EinschreibungId",
                table: "Rechnungspositionen",
                column: "EinschreibungId");

            migrationBuilder.CreateIndex(
                name: "IX_Rechnungspositionen_RechnungId",
                table: "Rechnungspositionen",
                column: "RechnungId");

            migrationBuilder.CreateIndex(
                name: "IX_Rechnungspositionen_SchuelerId",
                table: "Rechnungspositionen",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsableAnteile_AusgezahltVonId",
                table: "ResponsableAnteile",
                column: "AusgezahltVonId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsableAnteile_AusgleichFuerId",
                table: "ResponsableAnteile",
                column: "AusgleichFuerId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsableAnteile_RechnungId",
                table: "ResponsableAnteile",
                column: "RechnungId");

            migrationBuilder.CreateIndex(
                name: "IX_ResponsableAnteile_ResponsableId_AusgezahltAmUtc",
                table: "ResponsableAnteile",
                columns: new[] { "ResponsableId", "AusgezahltAmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResponsableAnteile_SchuleId",
                table: "ResponsableAnteile",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Rollen",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RollenClaims_RoleId",
                table: "RollenClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerProfile_KartenToken",
                table: "SchuelerProfile",
                column: "KartenToken",
                unique: true,
                filter: "[KartenToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerProfile_KontoId",
                table: "SchuelerProfile",
                column: "KontoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerProfile_SchuleId_Matrikel",
                table: "SchuelerProfile",
                columns: new[] { "SchuleId", "Matrikel" },
                unique: true,
                filter: "[Matrikel] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Schulen_LogoMediumId",
                table: "Schulen",
                column: "LogoMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_Schulen_NameNormalisiert",
                table: "Schulen",
                column: "NameNormalisiert",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schulen_ResponsableId",
                table: "Schulen",
                column: "ResponsableId");

            migrationBuilder.CreateIndex(
                name: "IX_Schulen_Status",
                table: "Schulen",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Schuljahre_EinLaufendesJahr",
                table: "Schuljahre",
                column: "SchuleId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Schuljahre_SchuleId_Bezeichnung",
                table: "Schuljahre",
                columns: new[] { "SchuleId", "Bezeichnung" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stufen_NachfolgerId",
                table: "Stufen",
                column: "NachfolgerId");

            migrationBuilder.CreateIndex(
                name: "IX_Stufen_SchuleId_Name",
                table: "Stufen",
                columns: new[] { "SchuleId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stundenplaene_KlasseId_GueltigAb",
                table: "Stundenplaene",
                columns: new[] { "KlasseId", "GueltigAb" });

            migrationBuilder.CreateIndex(
                name: "IX_Stundenplaene_SchuleId",
                table: "Stundenplaene",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_StundenplanEintraege_LehrauftragId_Wochentag",
                table: "StundenplanEintraege",
                columns: new[] { "LehrauftragId", "Wochentag" });

            migrationBuilder.CreateIndex(
                name: "IX_StundenplanEintraege_StundenplanId",
                table: "StundenplanEintraege",
                column: "StundenplanId");

            migrationBuilder.CreateIndex(
                name: "IX_Versuche_SchuelerId",
                table: "Versuche",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_Versuche_SchuleId",
                table: "Versuche",
                column: "SchuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Versuche_Status_DeadlineUtc",
                table: "Versuche",
                columns: new[] { "Status", "DeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Versuche_ZuweisungId_SchuelerId_Nr",
                table: "Versuche",
                columns: new[] { "ZuweisungId", "SchuelerId", "Nr" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungen_ErfasstVonId",
                table: "Zahlungen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungen_SchuelerId",
                table: "Zahlungen",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungen_SchuleId_Datum",
                table: "Zahlungen",
                columns: new[] { "SchuleId", "Datum" });

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungen_SchuleId_Quittungsnummer",
                table: "Zahlungen",
                columns: new[] { "SchuleId", "Quittungsnummer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungen_SchuljahrId_SchuelerId",
                table: "Zahlungen",
                columns: new[] { "SchuljahrId", "SchuelerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungen_StorniertVonId",
                table: "Zahlungen",
                column: "StorniertVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungsmeldungen_BelegMediumId",
                table: "Zahlungsmeldungen",
                column: "BelegMediumId");

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungsmeldungen_BuchungId",
                table: "Zahlungsmeldungen",
                column: "BuchungId");

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungsmeldungen_GemeldetVonId",
                table: "Zahlungsmeldungen",
                column: "GemeldetVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Zahlungsmeldungen_RechnungId",
                table: "Zahlungsmeldungen",
                column: "RechnungId");

            migrationBuilder.CreateIndex(
                name: "IX_Zuweisungen_KlasseId_Status",
                table: "Zuweisungen",
                columns: new[] { "KlasseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Zuweisungen_LeconId",
                table: "Zuweisungen",
                column: "LeconId");

            migrationBuilder.CreateIndex(
                name: "IX_Zuweisungen_LehrerId",
                table: "Zuweisungen",
                column: "LehrerId");

            migrationBuilder.CreateIndex(
                name: "IX_Zuweisungen_PruefungId",
                table: "Zuweisungen",
                column: "PruefungId");

            migrationBuilder.CreateIndex(
                name: "IX_Zuweisungen_SchuleId",
                table: "Zuweisungen",
                column: "SchuleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Ankuendigungen_Konten_ErstelltVonId",
                table: "Ankuendigungen",
                column: "ErstelltVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ankuendigungen_Schulen_SchuleId",
                table: "Ankuendigungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Antworten_Fragen_FrageId",
                table: "Antworten",
                column: "FrageId",
                principalTable: "Fragen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Antworten_Konten_KorrigiertVonId",
                table: "Antworten",
                column: "KorrigiertVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Antworten_Versuche_VersuchId",
                table: "Antworten",
                column: "VersuchId",
                principalTable: "Versuche",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Anwesenheiten_Einschreibungen_EinschreibungId",
                table: "Anwesenheiten",
                column: "EinschreibungId",
                principalTable: "Einschreibungen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Anwesenheiten_Konten_ErfasstVonId",
                table: "Anwesenheiten",
                column: "ErfasstVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Anwesenheiten_Schulen_SchuleId",
                table: "Anwesenheiten",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AufgabenBloecke_Lecons_LeconId",
                table: "AufgabenBloecke",
                column: "LeconId",
                principalTable: "Lecons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AufgabenBloecke_Pruefungen_PruefungId",
                table: "AufgabenBloecke",
                column: "PruefungId",
                principalTable: "Pruefungen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AufgabenBloecke_Schulen_SchuleId",
                table: "AufgabenBloecke",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bewertungen_Klassenfaecher_KlassenfachId",
                table: "Bewertungen",
                column: "KlassenfachId",
                principalTable: "Klassenfaecher",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bewertungen_Konten_ErstelltVonId",
                table: "Bewertungen",
                column: "ErstelltVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bewertungen_PeriodenKomponenten_KomponenteId",
                table: "Bewertungen",
                column: "KomponenteId",
                principalTable: "PeriodenKomponenten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bewertungen_Perioden_PeriodeId",
                table: "Bewertungen",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bewertungen_Schulen_SchuleId",
                table: "Bewertungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Buchungen_Kategorien_KategorieId",
                table: "Buchungen",
                column: "KategorieId",
                principalTable: "Kategorien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Buchungen_Konten_ErfasstVonId",
                table: "Buchungen",
                column: "ErfasstVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Buchungen_Medien_BelegMediumId",
                table: "Buchungen",
                column: "BelegMediumId",
                principalTable: "Medien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Buchungen_Schulen_SchuleId",
                table: "Buchungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bulletins_Einschreibungen_EinschreibungId",
                table: "Bulletins",
                column: "EinschreibungId",
                principalTable: "Einschreibungen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bulletins_Medien_FotoMediumId",
                table: "Bulletins",
                column: "FotoMediumId",
                principalTable: "Medien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bulletins_Medien_LogoMediumId",
                table: "Bulletins",
                column: "LogoMediumId",
                principalTable: "Medien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bulletins_Perioden_PeriodeId",
                table: "Bulletins",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Bulletins_Schulen_SchuleId",
                table: "Bulletins",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BulletinUebergaben_Konten_VonId",
                table: "BulletinUebergaben",
                column: "VonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BulletinUebergaben_Schulen_SchuleId",
                table: "BulletinUebergaben",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BulletinVorlagen_Schulen_SchuleId",
                table: "BulletinVorlagen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BulletinVorlagen_Stufen_StufeId",
                table: "BulletinVorlagen",
                column: "StufeId",
                principalTable: "Stufen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Einschreibungen_Klassen_KlasseId",
                table: "Einschreibungen",
                column: "KlasseId",
                principalTable: "Klassen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Einschreibungen_Konten_SchuelerId",
                table: "Einschreibungen",
                column: "SchuelerId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Einschreibungen_Schulen_SchuleId",
                table: "Einschreibungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Einschreibungen_Schuljahre_SchuljahrId",
                table: "Einschreibungen",
                column: "SchuljahrId",
                principalTable: "Schuljahre",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ElternKinder_Konten_ElternId",
                table: "ElternKinder",
                column: "ElternId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ElternKinder_Konten_SchuelerId",
                table: "ElternKinder",
                column: "SchuelerId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ElternKinder_Schulen_SchuleId",
                table: "ElternKinder",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ermaessigungen_GebuehrenPosten_PostenId",
                table: "Ermaessigungen",
                column: "PostenId",
                principalTable: "GebuehrenPosten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ermaessigungen_Konten_ErfasstVonId",
                table: "Ermaessigungen",
                column: "ErfasstVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ermaessigungen_Konten_SchuelerId",
                table: "Ermaessigungen",
                column: "SchuelerId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ermaessigungen_Schulen_SchuleId",
                table: "Ermaessigungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Ermaessigungen_Schuljahre_SchuljahrId",
                table: "Ermaessigungen",
                column: "SchuljahrId",
                principalTable: "Schuljahre",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FachBemerkungen_Klassenfaecher_KlassenfachId",
                table: "FachBemerkungen",
                column: "KlassenfachId",
                principalTable: "Klassenfaecher",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FachBemerkungen_Konten_ErfasstVonId",
                table: "FachBemerkungen",
                column: "ErfasstVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FachBemerkungen_Perioden_PeriodeId",
                table: "FachBemerkungen",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FachBemerkungen_Schulen_SchuleId",
                table: "FachBemerkungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Faecher_Schulen_SchuleId",
                table: "Faecher",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FormelFavoriten_Konten_KontoId",
                table: "FormelFavoriten",
                column: "KontoId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Fragen_Schulen_SchuleId",
                table: "Fragen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GebuehrenPosten_Klassen_KlasseId",
                table: "GebuehrenPosten",
                column: "KlasseId",
                principalTable: "Klassen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GebuehrenPosten_Schulen_SchuleId",
                table: "GebuehrenPosten",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GebuehrenPosten_Schuljahre_SchuljahrId",
                table: "GebuehrenPosten",
                column: "SchuljahrId",
                principalTable: "Schuljahre",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_GebuehrenPosten_Stufen_StufeId",
                table: "GebuehrenPosten",
                column: "StufeId",
                principalTable: "Stufen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Jahresentscheidungen_Schulen_SchuleId",
                table: "Jahresentscheidungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Jahresentscheidungen_Stufen_ZielStufeId",
                table: "Jahresentscheidungen",
                column: "ZielStufeId",
                principalTable: "Stufen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Kategorien_Schulen_SchuleId",
                table: "Kategorien",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Klassen_Konten_KlassenlehrerId",
                table: "Klassen",
                column: "KlassenlehrerId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Klassen_Perioden_PeriodeId",
                table: "Klassen",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Klassen_Schulen_SchuleId",
                table: "Klassen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Klassen_Schuljahre_SchuljahrId",
                table: "Klassen",
                column: "SchuljahrId",
                principalTable: "Schuljahre",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Klassen_Stufen_StufeId",
                table: "Klassen",
                column: "StufeId",
                principalTable: "Stufen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Klassenfaecher_Schulen_SchuleId",
                table: "Klassenfaecher",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KlassenratEintraege_Konten_ErfasstVonId",
                table: "KlassenratEintraege",
                column: "ErfasstVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KlassenratEintraege_Perioden_PeriodeId",
                table: "KlassenratEintraege",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KlassenratEintraege_Schulen_SchuleId",
                table: "KlassenratEintraege",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KlassenVeroeffentlichungen_Konten_VeroeffentlichtVonId",
                table: "KlassenVeroeffentlichungen",
                column: "VeroeffentlichtVonId",
                principalTable: "Konten",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KlassenVeroeffentlichungen_Perioden_PeriodeId",
                table: "KlassenVeroeffentlichungen",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KlassenVeroeffentlichungen_Schulen_SchuleId",
                table: "KlassenVeroeffentlichungen",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Konten_Medien_FotoMediumId",
                table: "Konten",
                column: "FotoMediumId",
                principalTable: "Medien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Konten_Schulen_SchuleId",
                table: "Konten",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KorrekturEintraege_Schulen_SchuleId",
                table: "KorrekturEintraege",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KorrekturEintraege_Versuche_VersuchId",
                table: "KorrekturEintraege",
                column: "VersuchId",
                principalTable: "Versuche",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lecons_Medien_VideoMediumId",
                table: "Lecons",
                column: "VideoMediumId",
                principalTable: "Medien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lecons_Schulen_SchuleId",
                table: "Lecons",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lecons_Stufen_StufeId",
                table: "Lecons",
                column: "StufeId",
                principalTable: "Stufen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lehrauftraege_Schulen_SchuleId",
                table: "Lehrauftraege",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LernStaende_Schulen_SchuleId",
                table: "LernStaende",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LernStaende_Zuweisungen_ZuweisungId",
                table: "LernStaende",
                column: "ZuweisungId",
                principalTable: "Zuweisungen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Medien_Schulen_SchuleId",
                table: "Medien",
                column: "SchuleId",
                principalTable: "Schulen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Medien_Konten_BesitzerId",
                table: "Medien");

            migrationBuilder.DropForeignKey(
                name: "FK_Schulen_Konten_ResponsableId",
                table: "Schulen");

            migrationBuilder.DropForeignKey(
                name: "FK_Medien_Schulen_SchuleId",
                table: "Medien");

            migrationBuilder.DropTable(
                name: "Ankuendigungen");

            migrationBuilder.DropTable(
                name: "Anwesenheiten");

            migrationBuilder.DropTable(
                name: "AuditEintraege");

            migrationBuilder.DropTable(
                name: "BulletinUebergaben");

            migrationBuilder.DropTable(
                name: "BulletinVorlagen");

            migrationBuilder.DropTable(
                name: "ElternKinder");

            migrationBuilder.DropTable(
                name: "Ermaessigungen");

            migrationBuilder.DropTable(
                name: "FachBemerkungen");

            migrationBuilder.DropTable(
                name: "FehlerProtokolle");

            migrationBuilder.DropTable(
                name: "FormelFavoriten");

            migrationBuilder.DropTable(
                name: "FrageOptionen");

            migrationBuilder.DropTable(
                name: "GebuehrenRaten");

            migrationBuilder.DropTable(
                name: "Jahresentscheidungen");

            migrationBuilder.DropTable(
                name: "KlassenratEintraege");

            migrationBuilder.DropTable(
                name: "KlassenVeroeffentlichungen");

            migrationBuilder.DropTable(
                name: "KontoClaims");

            migrationBuilder.DropTable(
                name: "KontoLogins");

            migrationBuilder.DropTable(
                name: "KontoRollen");

            migrationBuilder.DropTable(
                name: "KontoTokens");

            migrationBuilder.DropTable(
                name: "KorrekturEintraege");

            migrationBuilder.DropTable(
                name: "LeconSeiten");

            migrationBuilder.DropTable(
                name: "LernStaende");

            migrationBuilder.DropTable(
                name: "MailAusgaenge");

            migrationBuilder.DropTable(
                name: "MedienNutzungen");

            migrationBuilder.DropTable(
                name: "Nachrichten");

            migrationBuilder.DropTable(
                name: "NotenAenderungen");

            migrationBuilder.DropTable(
                name: "Nummernkreise");

            migrationBuilder.DropTable(
                name: "PlattformEinstellungen");

            migrationBuilder.DropTable(
                name: "Rechnungspositionen");

            migrationBuilder.DropTable(
                name: "ResponsableAnteile");

            migrationBuilder.DropTable(
                name: "RollenClaims");

            migrationBuilder.DropTable(
                name: "SchuelerProfile");

            migrationBuilder.DropTable(
                name: "StundenplanEintraege");

            migrationBuilder.DropTable(
                name: "Zahlungen");

            migrationBuilder.DropTable(
                name: "Zahlungsmeldungen");

            migrationBuilder.DropTable(
                name: "Bulletins");

            migrationBuilder.DropTable(
                name: "GebuehrenPosten");

            migrationBuilder.DropTable(
                name: "Antworten");

            migrationBuilder.DropTable(
                name: "Noten");

            migrationBuilder.DropTable(
                name: "Rollen");

            migrationBuilder.DropTable(
                name: "Lehrauftraege");

            migrationBuilder.DropTable(
                name: "Stundenplaene");

            migrationBuilder.DropTable(
                name: "Buchungen");

            migrationBuilder.DropTable(
                name: "Plattformrechnungen");

            migrationBuilder.DropTable(
                name: "Fragen");

            migrationBuilder.DropTable(
                name: "Versuche");

            migrationBuilder.DropTable(
                name: "Bewertungen");

            migrationBuilder.DropTable(
                name: "Einschreibungen");

            migrationBuilder.DropTable(
                name: "Kategorien");

            migrationBuilder.DropTable(
                name: "AufgabenBloecke");

            migrationBuilder.DropTable(
                name: "Zuweisungen");

            migrationBuilder.DropTable(
                name: "Klassenfaecher");

            migrationBuilder.DropTable(
                name: "PeriodenKomponenten");

            migrationBuilder.DropTable(
                name: "Lecons");

            migrationBuilder.DropTable(
                name: "Pruefungen");

            migrationBuilder.DropTable(
                name: "Klassen");

            migrationBuilder.DropTable(
                name: "Faecher");

            migrationBuilder.DropTable(
                name: "Perioden");

            migrationBuilder.DropTable(
                name: "Stufen");

            migrationBuilder.DropTable(
                name: "Schuljahre");

            migrationBuilder.DropTable(
                name: "Konten");

            migrationBuilder.DropTable(
                name: "Schulen");

            migrationBuilder.DropTable(
                name: "Medien");
        }
    }
}
