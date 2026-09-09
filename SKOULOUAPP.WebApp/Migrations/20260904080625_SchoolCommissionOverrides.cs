using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class SchoolCommissionOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ProductOwnerCommissionPercentOverride",
                table: "Schools",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SchoolLeaderCommissionPercentOverride",
                table: "Schools",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeacherCommissionPercentOverride",
                table: "Schools",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProductOwnerCommissionPercentOverride",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SchoolLeaderCommissionPercentOverride",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "TeacherCommissionPercentOverride",
                table: "Schools");
        }
    }
}
