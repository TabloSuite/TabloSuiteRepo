using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class CycleSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LevelEnrollments_StudentId_KlasseId",
                table: "LevelEnrollments");

            migrationBuilder.AddColumn<Guid>(
                name: "DurchlaufId",
                table: "LevelEnrollments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IstAktuell",
                table: "LevelEnrollments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE [LevelEnrollments] SET [IstAktuell] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_DurchlaufId",
                table: "LevelEnrollments",
                column: "DurchlaufId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_StudentId_DurchlaufId",
                table: "LevelEnrollments",
                columns: new[] { "StudentId", "DurchlaufId" },
                unique: true,
                filter: "[DurchlaufId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_StudentId_KlasseId",
                table: "LevelEnrollments",
                columns: new[] { "StudentId", "KlasseId" },
                unique: true,
                filter: "[IstAktuell] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_LevelEnrollments_KursNiveauDurchlaeufe_DurchlaufId",
                table: "LevelEnrollments",
                column: "DurchlaufId",
                principalTable: "KursNiveauDurchlaeufe",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LevelEnrollments_KursNiveauDurchlaeufe_DurchlaufId",
                table: "LevelEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_LevelEnrollments_DurchlaufId",
                table: "LevelEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_LevelEnrollments_StudentId_DurchlaufId",
                table: "LevelEnrollments");

            migrationBuilder.DropIndex(
                name: "IX_LevelEnrollments_StudentId_KlasseId",
                table: "LevelEnrollments");

            migrationBuilder.DropColumn(
                name: "DurchlaufId",
                table: "LevelEnrollments");

            migrationBuilder.DropColumn(
                name: "IstAktuell",
                table: "LevelEnrollments");

            migrationBuilder.CreateIndex(
                name: "IX_LevelEnrollments_StudentId_KlasseId",
                table: "LevelEnrollments",
                columns: new[] { "StudentId", "KlasseId" },
                unique: true);
        }
    }
}
