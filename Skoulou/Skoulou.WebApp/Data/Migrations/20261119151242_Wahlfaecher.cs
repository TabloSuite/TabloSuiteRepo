using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoulou.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Wahlfaecher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IstWahlfach",
                table: "Klassenfaecher",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "WahlfachRegel",
                table: "Klassenfaecher",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Fachwahlen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KlassenfachId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErfasstVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fachwahlen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fachwahlen_Klassenfaecher_KlassenfachId",
                        column: x => x.KlassenfachId,
                        principalTable: "Klassenfaecher",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fachwahlen_Konten_ErfasstVonId",
                        column: x => x.ErfasstVonId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fachwahlen_Konten_SchuelerId",
                        column: x => x.SchuelerId,
                        principalTable: "Konten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Fachwahlen_Schulen_SchuleId",
                        column: x => x.SchuleId,
                        principalTable: "Schulen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fachwahlen_ErfasstVonId",
                table: "Fachwahlen",
                column: "ErfasstVonId");

            migrationBuilder.CreateIndex(
                name: "IX_Fachwahlen_KlassenfachId_SchuelerId",
                table: "Fachwahlen",
                columns: new[] { "KlassenfachId", "SchuelerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fachwahlen_SchuelerId",
                table: "Fachwahlen",
                column: "SchuelerId");

            migrationBuilder.CreateIndex(
                name: "IX_Fachwahlen_SchuleId",
                table: "Fachwahlen",
                column: "SchuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fachwahlen");

            migrationBuilder.DropColumn(
                name: "IstWahlfach",
                table: "Klassenfaecher");

            migrationBuilder.DropColumn(
                name: "WahlfachRegel",
                table: "Klassenfaecher");
        }
    }
}
