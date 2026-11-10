using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class EmailSchalter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailRegeln",
                columns: table => new
                {
                    Kategorie = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Aktiv = table.Column<bool>(type: "bit", nullable: false),
                    GeaendertUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailRegeln", x => x.Kategorie);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailRegeln");
        }
    }
}
