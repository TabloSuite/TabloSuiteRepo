using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class SchulbezogeneKlassenOhneSprachgruppen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CEFRGroup",
                table: "Klassen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CEFRGroup",
                table: "Klassen",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
