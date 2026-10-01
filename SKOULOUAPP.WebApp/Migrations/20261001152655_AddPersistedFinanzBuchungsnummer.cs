using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HalloApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistedFinanzBuchungsnummer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Buchungsnummer",
                table: "FinanzBuchungen",
                type: "nvarchar(34)",
                maxLength: 34,
                nullable: false,
                computedColumnSql: "'B-' + REPLACE(CONVERT(varchar(36), [Id]), '-', '')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanzBuchungen_Buchungsnummer",
                table: "FinanzBuchungen",
                column: "Buchungsnummer",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinanzBuchungen_Buchungsnummer",
                table: "FinanzBuchungen");

            migrationBuilder.DropColumn(
                name: "Buchungsnummer",
                table: "FinanzBuchungen");
        }
    }
}
