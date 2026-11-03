using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class GeraetSalonFavorit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GeraetSalonFavoriten",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeraeteSchluessel = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SalonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeraeteName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeraetSalonFavoriten", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeraetSalonFavoriten_Salons_SalonId",
                        column: x => x.SalonId,
                        principalTable: "Salons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeraetSalonFavoriten_GeraeteSchluessel",
                table: "GeraetSalonFavoriten",
                column: "GeraeteSchluessel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GeraetSalonFavoriten_SalonId",
                table: "GeraetSalonFavoriten",
                column: "SalonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeraetSalonFavoriten");
        }
    }
}
