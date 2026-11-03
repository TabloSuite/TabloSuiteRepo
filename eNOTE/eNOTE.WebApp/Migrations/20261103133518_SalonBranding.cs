using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class SalonBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LogoAssetId",
                table: "Salons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slogan",
                table: "Salons",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Salons_LogoAssetId",
                table: "Salons",
                column: "LogoAssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_Salons_BlobMediaAssets_LogoAssetId",
                table: "Salons",
                column: "LogoAssetId",
                principalTable: "BlobMediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Salons_BlobMediaAssets_LogoAssetId",
                table: "Salons");

            migrationBuilder.DropIndex(
                name: "IX_Salons_LogoAssetId",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "LogoAssetId",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "Slogan",
                table: "Salons");
        }
    }
}
