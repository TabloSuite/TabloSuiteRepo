using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class PlattformEinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlattformEinstellungen",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    KiFuerAlleRollen = table.Column<bool>(type: "bit", nullable: false),
                    GeaendertUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlattformEinstellungen", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlattformEinstellungen");
        }
    }
}
