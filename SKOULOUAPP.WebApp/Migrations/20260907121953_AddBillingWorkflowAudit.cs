using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingWorkflowAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BestaetigtVonSuperAdminId",
                table: "Abrechnungsperioden",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ErstelltVonUserId",
                table: "Abrechnungsperioden",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BestaetigtVonSuperAdminId",
                table: "Abrechnungsperioden");

            migrationBuilder.DropColumn(
                name: "ErstelltVonUserId",
                table: "Abrechnungsperioden");
        }
    }
}
