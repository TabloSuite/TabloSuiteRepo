using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSchoolType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchulTyp",
                table: "Schools");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchulTyp",
                table: "Schools",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}
