using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class KategorieUnterkategorien : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "LeistungKategorien",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeistungKategorien_ParentId",
                table: "LeistungKategorien",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeistungKategorien_LeistungKategorien_ParentId",
                table: "LeistungKategorien",
                column: "ParentId",
                principalTable: "LeistungKategorien",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeistungKategorien_LeistungKategorien_ParentId",
                table: "LeistungKategorien");

            migrationBuilder.DropIndex(
                name: "IX_LeistungKategorien_ParentId",
                table: "LeistungKategorien");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "LeistungKategorien");
        }
    }
}
