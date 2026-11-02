using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class SchulDokumentStammdaten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Geburtsort",
                table: "StudentProfiles",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Matrikelnummer",
                table: "StudentProfiles",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DokumentAdresse",
                table: "Schools",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DokumentEmail",
                table: "Schools",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DokumentTelefon",
                table: "Schools",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ministerium",
                table: "Schools",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OffizielleBezeichnung",
                table: "Schools",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepublikZeile",
                table: "Schools",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slogan",
                table: "Schools",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StaatsMotto",
                table: "Schools",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Zulassungsnummer",
                table: "Schools",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_SchoolId_Matrikelnummer",
                table: "StudentProfiles",
                columns: new[] { "SchoolId", "Matrikelnummer" },
                unique: true,
                filter: "[Matrikelnummer] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentProfiles_SchoolId_Matrikelnummer",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "Geburtsort",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "Matrikelnummer",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "DokumentAdresse",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DokumentEmail",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DokumentTelefon",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Ministerium",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "OffizielleBezeichnung",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "RepublikZeile",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Slogan",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "StaatsMotto",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "Zulassungsnummer",
                table: "Schools");
        }
    }
}
