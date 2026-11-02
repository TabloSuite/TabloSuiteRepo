using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class Bewertungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BewertungsPeriode",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Titel = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Art = table.Column<int>(type: "int", nullable: false),
                    Reihenfolge = table.Column<int>(type: "int", nullable: false),
                    JahresGewicht = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    IstGeschlossen = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BewertungsPeriode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BewertungsPeriode_Schuljahr_SchuljahrID_SchulID",
                        columns: x => new { x.SchuljahrID, x.SchulID },
                        principalTable: "Schuljahr",
                        principalColumns: new[] { "Id", "SchulID" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FachBewertung",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FachID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Koeffizient = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FachBewertung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FachBewertung_Faecher_FachID",
                        column: x => x.FachID,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FachBewertung_Klassen_KlasseID",
                        column: x => x.KlasseID,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FachBewertung_Schuljahr_SchuljahrID",
                        column: x => x.SchuljahrID,
                        principalTable: "Schuljahr",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BewertungsBestandteil",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Gewicht = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    MaximalPunkte = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BewertungsBestandteil", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BewertungsBestandteil_BewertungsPeriode_PeriodeID",
                        column: x => x.PeriodeID,
                        principalTable: "BewertungsPeriode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FachNote",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BestandteilID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FachBewertungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JahresZuordnungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Punkte = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    Bemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BearbeitetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FachNote", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FachNote_BewertungsBestandteil_BestandteilID",
                        column: x => x.BestandteilID,
                        principalTable: "BewertungsBestandteil",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FachNote_FachBewertung_FachBewertungID",
                        column: x => x.FachBewertungID,
                        principalTable: "FachBewertung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FachNote_SchuelerJahresZuordnung_JahresZuordnungID",
                        column: x => x.JahresZuordnungID,
                        principalTable: "SchuelerJahresZuordnung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BewertungsBestandteil_PeriodeID_Bezeichnung",
                table: "BewertungsBestandteil",
                columns: new[] { "PeriodeID", "Bezeichnung" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BewertungsPeriode_SchuljahrID_SchulID",
                table: "BewertungsPeriode",
                columns: new[] { "SchuljahrID", "SchulID" });

            migrationBuilder.CreateIndex(
                name: "IX_BewertungsPeriode_SchuljahrID_Titel",
                table: "BewertungsPeriode",
                columns: new[] { "SchuljahrID", "Titel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FachBewertung_FachID",
                table: "FachBewertung",
                column: "FachID");

            migrationBuilder.CreateIndex(
                name: "IX_FachBewertung_KlasseID",
                table: "FachBewertung",
                column: "KlasseID");

            migrationBuilder.CreateIndex(
                name: "IX_FachBewertung_SchuljahrID_KlasseID_FachID",
                table: "FachBewertung",
                columns: new[] { "SchuljahrID", "KlasseID", "FachID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FachNote_BestandteilID_FachBewertungID_JahresZuordnungID",
                table: "FachNote",
                columns: new[] { "BestandteilID", "FachBewertungID", "JahresZuordnungID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FachNote_FachBewertungID",
                table: "FachNote",
                column: "FachBewertungID");

            migrationBuilder.CreateIndex(
                name: "IX_FachNote_JahresZuordnungID",
                table: "FachNote",
                column: "JahresZuordnungID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FachNote");

            migrationBuilder.DropTable(
                name: "BewertungsBestandteil");

            migrationBuilder.DropTable(
                name: "FachBewertung");

            migrationBuilder.DropTable(
                name: "BewertungsPeriode");
        }
    }
}
