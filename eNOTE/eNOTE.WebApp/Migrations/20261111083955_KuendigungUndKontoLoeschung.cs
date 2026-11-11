using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class KuendigungUndKontoLoeschung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GekuendigtAmUtc",
                table: "Salons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GekuendigtZumUtc",
                table: "Salons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GeloeschtUtc",
                table: "Salons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KuendigungGrund",
                table: "Salons",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoeschungAbUtc",
                table: "Salons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoeschungBeantragtUtc",
                table: "Salons",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GekuendigtAmUtc",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "GekuendigtZumUtc",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "GeloeschtUtc",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "KuendigungGrund",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "LoeschungAbUtc",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "LoeschungBeantragtUtc",
                table: "Salons");
        }
    }
}
