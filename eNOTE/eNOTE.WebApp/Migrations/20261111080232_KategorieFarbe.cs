using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class KategorieFarbe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Farbe",
                table: "LeistungKategorien",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Farbe",
                table: "LeistungKategorien");
        }
    }
}
