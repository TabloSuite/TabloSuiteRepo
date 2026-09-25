using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFormelFavoriten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormelFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BenutzerID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaTeX = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Bezeichnung = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Vorschau = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormelFavoriten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormelFavoriten_AspNetUsers_BenutzerID",
                        column: x => x.BenutzerID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormelFavoriten_BenutzerID_LaTeX",
                table: "FormelFavoriten",
                columns: new[] { "BenutzerID", "LaTeX" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormelFavoriten");
        }
    }
}
