using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class BulletinAusgaben : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulletinAusgabe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JahresZuordnungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IstVeroeffentlicht = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AenderungsGrund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinAusgabe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulletinAusgabe_BewertungsPeriode_PeriodeID",
                        column: x => x.PeriodeID,
                        principalTable: "BewertungsPeriode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulletinAusgabe_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulletinAusgabe_SchuelerJahresZuordnung_JahresZuordnungID",
                        column: x => x.JahresZuordnungID,
                        principalTable: "SchuelerJahresZuordnung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BulletinVorlage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AkzentFarbe = table.Column<string>(type: "nvarchar(7)", maxLength: 7, nullable: false),
                    ZeigeKoeffizienten = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeRang = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeVorperioden = table.Column<bool>(type: "bit", nullable: false),
                    ZeigeUnterschriften = table.Column<bool>(type: "bit", nullable: false),
                    FussText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinVorlage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulletinVorlage_Klassen_KlasseID",
                        column: x => x.KlasseID,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BulletinVorlage_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulletinAusgabe_JahresZuordnungID",
                table: "BulletinAusgabe",
                column: "JahresZuordnungID");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinAusgabe_PeriodeID_JahresZuordnungID",
                table: "BulletinAusgabe",
                columns: new[] { "PeriodeID", "JahresZuordnungID" },
                unique: true,
                filter: "[IstVeroeffentlicht] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinAusgabe_PeriodeID_JahresZuordnungID_Version",
                table: "BulletinAusgabe",
                columns: new[] { "PeriodeID", "JahresZuordnungID", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BulletinAusgabe_SchulID",
                table: "BulletinAusgabe",
                column: "SchulID");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinVorlage_KlasseID",
                table: "BulletinVorlage",
                column: "KlasseID");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinVorlage_SchulID_KlasseID_Art",
                table: "BulletinVorlage",
                columns: new[] { "SchulID", "KlasseID", "Art" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulletinAusgabe");

            migrationBuilder.DropTable(
                name: "BulletinVorlage");
        }
    }
}
