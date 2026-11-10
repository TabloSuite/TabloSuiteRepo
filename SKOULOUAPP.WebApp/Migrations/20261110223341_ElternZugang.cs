using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class ElternZugang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElternKind",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchulID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ElternID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchuelerID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Beziehung = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErstelltVonID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ErstelltAmUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeaendertAmUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElternKind", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ElternKind_AspNetUsers_ElternID",
                        column: x => x.ElternID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ElternKind_AspNetUsers_SchuelerID",
                        column: x => x.SchuelerID,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElternKind_ElternID_SchuelerID",
                table: "ElternKind",
                columns: new[] { "ElternID", "SchuelerID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElternKind_SchuelerID",
                table: "ElternKind",
                column: "SchuelerID");

            migrationBuilder.CreateIndex(
                name: "IX_ElternKind_SchulID",
                table: "ElternKind",
                column: "SchulID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElternKind");
        }
    }
}
