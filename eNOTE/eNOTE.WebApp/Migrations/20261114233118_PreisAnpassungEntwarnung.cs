using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class PreisAnpassungEntwarnung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AufgehobenUtc",
                table: "PreisAnpassungen",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AufgehobenVonId",
                table: "PreisAnpassungen",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntwarnungAnkuendigungId",
                table: "PreisAnpassungen",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntwarnungMails",
                table: "PreisAnpassungen",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AufgehobenUtc",
                table: "PreisAnpassungen");

            migrationBuilder.DropColumn(
                name: "AufgehobenVonId",
                table: "PreisAnpassungen");

            migrationBuilder.DropColumn(
                name: "EntwarnungAnkuendigungId",
                table: "PreisAnpassungen");

            migrationBuilder.DropColumn(
                name: "EntwarnungMails",
                table: "PreisAnpassungen");
        }
    }
}
