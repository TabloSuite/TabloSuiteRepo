using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentProfileArchiveFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "StudentProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "StudentProfiles");
        }
    }
}
