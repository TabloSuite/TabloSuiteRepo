using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class EndgueltigeBulletinsUndBerichtigungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ErsetztAusgabeID",
                table: "BulletinAusgabe",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ErsetztDurchID",
                table: "BulletinAusgabe",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IstEndgueltig",
                table: "BulletinAusgabe",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PruefToken",
                table: "BulletinAusgabe",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BulletinAbschluss",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodeID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinAbschluss", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulletinAbschluss_BewertungsPeriode_PeriodeID",
                        column: x => x.PeriodeID,
                        principalTable: "BewertungsPeriode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BulletinBerichtigung",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuljahrID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlasseID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Grund = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    EntwurfJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VorschauHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FreigegebenAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FreigegebenVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AbgebrochenAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinBerichtigung", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BulletinUebergabe",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AusgabeID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BearbeitetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IstAbholung = table.Column<bool>(type: "bit", nullable: false),
                    Vermerk = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulletinUebergabe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulletinUebergabe_BulletinAusgabe_AusgabeID",
                        column: x => x.AusgabeID,
                        principalTable: "BulletinAusgabe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulletinAusgabe_PruefToken",
                table: "BulletinAusgabe",
                column: "PruefToken",
                unique: true,
                filter: "[PruefToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BulletinAbschluss_PeriodeID_KlasseID",
                table: "BulletinAbschluss",
                columns: new[] { "PeriodeID", "KlasseID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BulletinBerichtigung_SchulID_SchuljahrID_KlasseID",
                table: "BulletinBerichtigung",
                columns: new[] { "SchulID", "SchuljahrID", "KlasseID" });

            migrationBuilder.CreateIndex(
                name: "IX_BulletinUebergabe_AusgabeID",
                table: "BulletinUebergabe",
                column: "AusgabeID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulletinAbschluss");

            migrationBuilder.DropTable(
                name: "BulletinBerichtigung");

            migrationBuilder.DropTable(
                name: "BulletinUebergabe");

            migrationBuilder.DropIndex(
                name: "IX_BulletinAusgabe_PruefToken",
                table: "BulletinAusgabe");

            migrationBuilder.DropColumn(
                name: "ErsetztAusgabeID",
                table: "BulletinAusgabe");

            migrationBuilder.DropColumn(
                name: "ErsetztDurchID",
                table: "BulletinAusgabe");

            migrationBuilder.DropColumn(
                name: "IstEndgueltig",
                table: "BulletinAusgabe");

            migrationBuilder.DropColumn(
                name: "PruefToken",
                table: "BulletinAusgabe");
        }
    }
}
