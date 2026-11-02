using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class Stundenplan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stundenplan",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    GueltigAb = table.Column<DateOnly>(type: "date", nullable: false),
                    GueltigBis = table.Column<DateOnly>(type: "date", nullable: false),
                    IstVeroeffentlicht = table.Column<bool>(type: "bit", nullable: false),
                    ZeilenVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stundenplan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stundenplan_Klassen_KlasseID",
                        column: x => x.KlasseID,
                        principalTable: "Klassen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stundenplan_Schuljahr_SchuljahrID_SchulID",
                        columns: x => new { x.SchuljahrID, x.SchulID },
                        principalTable: "Schuljahr",
                        principalColumns: new[] { "Id", "SchulID" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StundenplanEintrag",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StundenplanID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnterrichtsZuordnungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Wochentag = table.Column<int>(type: "int", nullable: false),
                    Beginn = table.Column<TimeOnly>(type: "time", nullable: false),
                    Ende = table.Column<TimeOnly>(type: "time", nullable: false),
                    Raum = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StundenplanEintrag", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StundenplanEintrag_Stundenplan_StundenplanID",
                        column: x => x.StundenplanID,
                        principalTable: "Stundenplan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StundenplanEintrag_UnterrichtsZuordnung_UnterrichtsZuordnungID",
                        column: x => x.UnterrichtsZuordnungID,
                        principalTable: "UnterrichtsZuordnung",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stundenplan_KlasseID",
                table: "Stundenplan",
                column: "KlasseID");

            migrationBuilder.CreateIndex(
                name: "IX_Stundenplan_SchulID_SchuljahrID_KlasseID",
                table: "Stundenplan",
                columns: new[] { "SchulID", "SchuljahrID", "KlasseID" });

            migrationBuilder.CreateIndex(
                name: "IX_Stundenplan_SchuljahrID_SchulID",
                table: "Stundenplan",
                columns: new[] { "SchuljahrID", "SchulID" });

            migrationBuilder.CreateIndex(
                name: "IX_StundenplanEintrag_StundenplanID_Wochentag_Beginn",
                table: "StundenplanEintrag",
                columns: new[] { "StundenplanID", "Wochentag", "Beginn" });

            migrationBuilder.CreateIndex(
                name: "IX_StundenplanEintrag_UnterrichtsZuordnungID",
                table: "StundenplanEintrag",
                column: "UnterrichtsZuordnungID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StundenplanEintrag");

            migrationBuilder.DropTable(
                name: "Stundenplan");
        }
    }
}
