using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddMailboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MailNachrichten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GesendetVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Absender = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Empfaenger = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Betreff = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Inhalt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostmarkMessageID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IstEingehend = table.Column<bool>(type: "bit", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailNachrichten", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MailNachrichten_PostmarkMessageID",
                table: "MailNachrichten",
                column: "PostmarkMessageID",
                unique: true,
                filter: "[PostmarkMessageID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MailNachrichten_SchulID_ErstelltAmUtc",
                table: "MailNachrichten",
                columns: new[] { "SchulID", "ErstelltAmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MailNachrichten");
        }
    }
}
