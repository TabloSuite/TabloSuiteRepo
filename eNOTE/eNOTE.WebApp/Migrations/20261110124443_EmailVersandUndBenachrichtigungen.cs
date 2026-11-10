using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class EmailVersandUndBenachrichtigungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProbeEndeMailUtc",
                table: "Salons",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ErinnerungMailUtc",
                table: "NutzungsAbos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RechnungMailUtc",
                table: "NutzungsAbos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SperrMailUtc",
                table: "NutzungsAbos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Empfaenger",
                table: "MailNachrichten",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Betreff",
                table: "MailNachrichten",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "AnbieterId",
                table: "MailNachrichten",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kategorie",
                table: "MailNachrichten",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LetzterFehler",
                table: "MailNachrichten",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Versuche",
                table: "MailNachrichten",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MailNachrichten_IstGesendet_CreatedAtUtc",
                table: "MailNachrichten",
                columns: new[] { "IstGesendet", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MailNachrichten_IstGesendet_CreatedAtUtc",
                table: "MailNachrichten");

            migrationBuilder.DropColumn(
                name: "ProbeEndeMailUtc",
                table: "Salons");

            migrationBuilder.DropColumn(
                name: "ErinnerungMailUtc",
                table: "NutzungsAbos");

            migrationBuilder.DropColumn(
                name: "RechnungMailUtc",
                table: "NutzungsAbos");

            migrationBuilder.DropColumn(
                name: "SperrMailUtc",
                table: "NutzungsAbos");

            migrationBuilder.DropColumn(
                name: "AnbieterId",
                table: "MailNachrichten");

            migrationBuilder.DropColumn(
                name: "Kategorie",
                table: "MailNachrichten");

            migrationBuilder.DropColumn(
                name: "LetzterFehler",
                table: "MailNachrichten");

            migrationBuilder.DropColumn(
                name: "Versuche",
                table: "MailNachrichten");

            migrationBuilder.AlterColumn<string>(
                name: "Empfaenger",
                table: "MailNachrichten",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320);

            migrationBuilder.AlterColumn<string>(
                name: "Betreff",
                table: "MailNachrichten",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300);
        }
    }
}
