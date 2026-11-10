using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class Wartungsfenster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "WartungBisUtc",
                table: "Ankuendigungen",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WartungVonUtc",
                table: "Ankuendigungen",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WartungBisUtc",
                table: "Ankuendigungen");

            migrationBuilder.DropColumn(
                name: "WartungVonUtc",
                table: "Ankuendigungen");
        }
    }
}
