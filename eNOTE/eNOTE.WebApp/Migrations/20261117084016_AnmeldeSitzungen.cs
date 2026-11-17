using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AnmeldeSitzungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnmeldeSitzungen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Stempel = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Geraet = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ErstelltUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ZuletztUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BeendetUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnmeldeSitzungen", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnmeldeSitzungen_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnmeldeSitzungen_UserId_BeendetUtc",
                table: "AnmeldeSitzungen",
                columns: new[] { "UserId", "BeendetUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnmeldeSitzungen");
        }
    }
}
