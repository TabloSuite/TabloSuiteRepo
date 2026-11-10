using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class Kundenkartei : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KundeId",
                table: "Verkaeufe",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Kunden",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Telefon = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    TelefonZiffern = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TelefonEinwilligungUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notiz = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ErstelltVonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GeaendertUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kunden", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kunden_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Verkaeufe_KundeId",
                table: "Verkaeufe",
                column: "KundeId");

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_SalonId_Name",
                table: "Kunden",
                columns: new[] { "SalonId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Kunden_SalonId_TelefonZiffern",
                table: "Kunden",
                columns: new[] { "SalonId", "TelefonZiffern" });

            migrationBuilder.AddForeignKey(
                name: "FK_Verkaeufe_Kunden_KundeId",
                table: "Verkaeufe",
                column: "KundeId",
                principalTable: "Kunden",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Verkaeufe_Kunden_KundeId",
                table: "Verkaeufe");

            migrationBuilder.DropTable(
                name: "Kunden");

            migrationBuilder.DropIndex(
                name: "IX_Verkaeufe_KundeId",
                table: "Verkaeufe");

            migrationBuilder.DropColumn(
                name: "KundeId",
                table: "Verkaeufe");
        }
    }
}
