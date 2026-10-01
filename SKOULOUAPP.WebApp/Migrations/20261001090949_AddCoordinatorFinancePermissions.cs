using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddCoordinatorFinancePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CanAccessFinances",
                table: "UserSchools",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanCreateFinanceEntries",
                table: "UserSchools",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageCyclePayments",
                table: "UserSchools",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanViewFinanceDashboard",
                table: "UserSchools",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanAccessFinances",
                table: "UserSchools");

            migrationBuilder.DropColumn(
                name: "CanCreateFinanceEntries",
                table: "UserSchools");

            migrationBuilder.DropColumn(
                name: "CanManageCyclePayments",
                table: "UserSchools");

            migrationBuilder.DropColumn(
                name: "CanViewFinanceDashboard",
                table: "UserSchools");
        }
    }
}
