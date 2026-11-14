using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoulou.WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessionGebuehrUndHauptAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IstHauptAdmin",
                table: "Konten",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PeriodeId",
                table: "GebuehrenPosten",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GebuehrenPosten_PeriodeId",
                table: "GebuehrenPosten",
                column: "PeriodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_GebuehrenPosten_Perioden_PeriodeId",
                table: "GebuehrenPosten",
                column: "PeriodeId",
                principalTable: "Perioden",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GebuehrenPosten_Perioden_PeriodeId",
                table: "GebuehrenPosten");

            migrationBuilder.DropIndex(
                name: "IX_GebuehrenPosten_PeriodeId",
                table: "GebuehrenPosten");

            migrationBuilder.DropColumn(
                name: "IstHauptAdmin",
                table: "Konten");

            migrationBuilder.DropColumn(
                name: "PeriodeId",
                table: "GebuehrenPosten");
        }
    }
}
