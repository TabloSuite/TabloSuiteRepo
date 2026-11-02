using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class NotenHistorieUndBulletinLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchriftGroesse",
                table: "BulletinVorlage",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "SeitenRand",
                table: "BulletinVorlage",
                type: "int",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeAnwesenheit",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ZeigeLehrkraefte",
                table: "BulletinVorlage",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "NotenAenderung",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FachNoteID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BearbeitetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BearbeiterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AltePunkte = table.Column<decimal>(type: "decimal(7,2)", nullable: true),
                    NeuePunkte = table.Column<decimal>(type: "decimal(7,2)", nullable: false),
                    AlteBemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    NeueBemerkung = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotenAenderung", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotenAenderung_FachNote_FachNoteID",
                        column: x => x.FachNoteID,
                        principalTable: "FachNote",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotenAenderung_FachNoteID_ErstelltAmUtc",
                table: "NotenAenderung",
                columns: new[] { "FachNoteID", "ErstelltAmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotenAenderung");

            migrationBuilder.DropColumn(
                name: "SchriftGroesse",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "SeitenRand",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeAnwesenheit",
                table: "BulletinVorlage");

            migrationBuilder.DropColumn(
                name: "ZeigeLehrkraefte",
                table: "BulletinVorlage");
        }
    }
}
