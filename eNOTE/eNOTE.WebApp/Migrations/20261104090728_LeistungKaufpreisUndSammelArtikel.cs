using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class LeistungKaufpreisUndSammelArtikel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Stueckzahl",
                table: "Leistungen",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<decimal>(
                name: "MindestbestandWarnung",
                table: "Leistungen",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BasisAbzugMenge",
                table: "Leistungen",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BasisLeistungId",
                table: "Leistungen",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kaufpreis",
                table: "Leistungen",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leistungen_BasisLeistungId",
                table: "Leistungen",
                column: "BasisLeistungId");

            migrationBuilder.AddForeignKey(
                name: "FK_Leistungen_Leistungen_BasisLeistungId",
                table: "Leistungen",
                column: "BasisLeistungId",
                principalTable: "Leistungen",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Leistungen_Leistungen_BasisLeistungId",
                table: "Leistungen");

            migrationBuilder.DropIndex(
                name: "IX_Leistungen_BasisLeistungId",
                table: "Leistungen");

            migrationBuilder.DropColumn(
                name: "BasisAbzugMenge",
                table: "Leistungen");

            migrationBuilder.DropColumn(
                name: "BasisLeistungId",
                table: "Leistungen");

            migrationBuilder.DropColumn(
                name: "Kaufpreis",
                table: "Leistungen");

            migrationBuilder.AlterColumn<int>(
                name: "Stueckzahl",
                table: "Leistungen",
                type: "int",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "MindestbestandWarnung",
                table: "Leistungen",
                type: "int",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
