using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class ProfilBildHinzugefuegt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfilBildBlobName",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfilBildBlobName",
                table: "AspNetUsers");
        }
    }
}
