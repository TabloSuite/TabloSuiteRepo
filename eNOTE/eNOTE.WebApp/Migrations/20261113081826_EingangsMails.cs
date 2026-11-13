using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class EingangsMails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EingangsMails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Absender = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Empfaenger = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnbieterId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EmpfangenUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IstGelesen = table.Column<bool>(type: "bit", nullable: false),
                    BeantwortetUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EingangsMails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EingangsMails_AnbieterId",
                table: "EingangsMails",
                column: "AnbieterId",
                unique: true,
                filter: "[AnbieterId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EingangsMails_IstGelesen_EmpfangenUtc",
                table: "EingangsMails",
                columns: new[] { "IstGelesen", "EmpfangenUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EingangsMails");
        }
    }
}
