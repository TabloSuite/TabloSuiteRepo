using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AutomatischeZyklusteilnahme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AufnahmeBestaetigtAmUtc",
                table: "StudentProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StorniertAmUtc",
                table: "LevelEnrollments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorniertVonID",
                table: "LevelEnrollments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StornoGrund",
                table: "LevelEnrollments",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PlattformPreisProSchueler",
                table: "KursNiveauDurchlaeufe",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AufnahmeBestaetigtAmUtc",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "StorniertAmUtc",
                table: "LevelEnrollments");

            migrationBuilder.DropColumn(
                name: "StorniertVonID",
                table: "LevelEnrollments");

            migrationBuilder.DropColumn(
                name: "StornoGrund",
                table: "LevelEnrollments");

            migrationBuilder.DropColumn(
                name: "PlattformPreisProSchueler",
                table: "KursNiveauDurchlaeufe");
        }
    }
}
