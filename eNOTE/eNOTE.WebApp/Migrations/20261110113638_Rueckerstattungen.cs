using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class Rueckerstattungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StornoVonPositionId",
                table: "VerkaufsPositionen",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StornoErfasstVonName",
                table: "Verkaeufe",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StornoGrund",
                table: "Verkaeufe",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StornoVonVerkaufId",
                table: "Verkaeufe",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerkaufsPositionen_StornoVonPositionId",
                table: "VerkaufsPositionen",
                column: "StornoVonPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_Verkaeufe_StornoVonVerkaufId",
                table: "Verkaeufe",
                column: "StornoVonVerkaufId");

            migrationBuilder.AddForeignKey(
                name: "FK_Verkaeufe_Verkaeufe_StornoVonVerkaufId",
                table: "Verkaeufe",
                column: "StornoVonVerkaufId",
                principalTable: "Verkaeufe",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Verkaeufe_Verkaeufe_StornoVonVerkaufId",
                table: "Verkaeufe");

            migrationBuilder.DropIndex(
                name: "IX_VerkaufsPositionen_StornoVonPositionId",
                table: "VerkaufsPositionen");

            migrationBuilder.DropIndex(
                name: "IX_Verkaeufe_StornoVonVerkaufId",
                table: "Verkaeufe");

            migrationBuilder.DropColumn(
                name: "StornoVonPositionId",
                table: "VerkaufsPositionen");

            migrationBuilder.DropColumn(
                name: "StornoErfasstVonName",
                table: "Verkaeufe");

            migrationBuilder.DropColumn(
                name: "StornoGrund",
                table: "Verkaeufe");

            migrationBuilder.DropColumn(
                name: "StornoVonVerkaufId",
                table: "Verkaeufe");
        }
    }
}
