using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ENote.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class PersoenlicheAnmeldung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KannSichAnmelden",
                table: "UserSalons",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KannSichAnmelden",
                table: "UserSalons");
        }
    }
}
