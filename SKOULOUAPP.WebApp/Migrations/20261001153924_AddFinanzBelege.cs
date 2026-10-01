using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanzBelege : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinanzBelege",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuchungID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Dateiname = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Inhaltstyp = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Inhalt = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanzBelege", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanzBelege_FinanzBuchungen_BuchungID",
                        column: x => x.BuchungID,
                        principalTable: "FinanzBuchungen",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBelege_BuchungID",
                table: "FinanzBelege",
                column: "BuchungID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBelege_SchulID",
                table: "FinanzBelege",
                column: "SchulID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanzBelege");
        }
    }
}
