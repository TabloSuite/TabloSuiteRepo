using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class UnterrichtsGrundlagen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Schuljahr",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Beginn = table.Column<DateOnly>(type: "date", nullable: false),
                    Ende = table.Column<DateOnly>(type: "date", nullable: false),
                    IstGeschlossen = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schuljahr", x => x.Id);
                    table.UniqueConstraint("AK_Schuljahr_Id_SchulID", x => new { x.Id, x.SchulID });
                    table.ForeignKey(
                        name: "FK_Schuljahr_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SchuelerJahresZuordnung",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IstWiederholer = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchuelerJahresZuordnung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchuelerJahresZuordnung_AspNetUsers_SchuelerID",
                        column: x => x.SchuelerID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchuelerJahresZuordnung_Klassen_KlasseID",
                        column: x => x.KlasseID,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchuelerJahresZuordnung_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SchuelerJahresZuordnung_Schuljahr_SchuljahrID_SchulID",
                        columns: x => new { x.SchuljahrID, x.SchulID },
                        principalTable: "Schuljahr",
                        principalColumns: new[] { "Id", "SchulID" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnterrichtsZuordnung",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FachID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LehrerID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Wochenstunden = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnterrichtsZuordnung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnterrichtsZuordnung_AspNetUsers_LehrerID",
                        column: x => x.LehrerID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnterrichtsZuordnung_Faecher_FachID",
                        column: x => x.FachID,
                        principalTable: "Faecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnterrichtsZuordnung_Klassen_KlasseID",
                        column: x => x.KlasseID,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnterrichtsZuordnung_Schools_SchulID",
                        column: x => x.SchulID,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnterrichtsZuordnung_Schuljahr_SchuljahrID_SchulID",
                        columns: x => new { x.SchuljahrID, x.SchulID },
                        principalTable: "Schuljahr",
                        principalColumns: new[] { "Id", "SchulID" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerJahresZuordnung_KlasseID",
                table: "SchuelerJahresZuordnung",
                column: "KlasseID");

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerJahresZuordnung_SchuelerID",
                table: "SchuelerJahresZuordnung",
                column: "SchuelerID");

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerJahresZuordnung_SchulID",
                table: "SchuelerJahresZuordnung",
                column: "SchulID");

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerJahresZuordnung_SchuljahrID_SchuelerID",
                table: "SchuelerJahresZuordnung",
                columns: new[] { "SchuljahrID", "SchuelerID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchuelerJahresZuordnung_SchuljahrID_SchulID",
                table: "SchuelerJahresZuordnung",
                columns: new[] { "SchuljahrID", "SchulID" });

            migrationBuilder.CreateIndex(
                name: "IX_Schuljahr_SchulID_Bezeichnung",
                table: "Schuljahr",
                columns: new[] { "SchulID", "Bezeichnung" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnterrichtsZuordnung_FachID",
                table: "UnterrichtsZuordnung",
                column: "FachID");

            migrationBuilder.CreateIndex(
                name: "IX_UnterrichtsZuordnung_KlasseID",
                table: "UnterrichtsZuordnung",
                column: "KlasseID");

            migrationBuilder.CreateIndex(
                name: "IX_UnterrichtsZuordnung_LehrerID",
                table: "UnterrichtsZuordnung",
                column: "LehrerID");

            migrationBuilder.CreateIndex(
                name: "IX_UnterrichtsZuordnung_SchulID",
                table: "UnterrichtsZuordnung",
                column: "SchulID");

            migrationBuilder.CreateIndex(
                name: "IX_UnterrichtsZuordnung_SchuljahrID_KlasseID_FachID_LehrerID",
                table: "UnterrichtsZuordnung",
                columns: new[] { "SchuljahrID", "KlasseID", "FachID", "LehrerID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnterrichtsZuordnung_SchuljahrID_SchulID",
                table: "UnterrichtsZuordnung",
                columns: new[] { "SchuljahrID", "SchulID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SchuelerJahresZuordnung");

            migrationBuilder.DropTable(
                name: "UnterrichtsZuordnung");

            migrationBuilder.DropTable(
                name: "Schuljahr");
        }
    }
}
