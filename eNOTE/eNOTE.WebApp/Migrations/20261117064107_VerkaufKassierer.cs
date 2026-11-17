using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class VerkaufKassierer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Verkaeufe_KassenSitzungId",
                table: "Verkaeufe");

            migrationBuilder.AddColumn<Guid>(
                name: "KassiererId",
                table: "Verkaeufe",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KassiererName",
                table: "Verkaeufe",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verkaeufe_KassenSitzungId_KassiererId",
                table: "Verkaeufe",
                columns: new[] { "KassenSitzungId", "KassiererId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Verkaeufe_KassenSitzungId_KassiererId",
                table: "Verkaeufe");

            migrationBuilder.DropColumn(
                name: "KassiererId",
                table: "Verkaeufe");

            migrationBuilder.DropColumn(
                name: "KassiererName",
                table: "Verkaeufe");

            migrationBuilder.CreateIndex(
                name: "IX_Verkaeufe_KassenSitzungId",
                table: "Verkaeufe",
                column: "KassenSitzungId");
        }
    }
}
