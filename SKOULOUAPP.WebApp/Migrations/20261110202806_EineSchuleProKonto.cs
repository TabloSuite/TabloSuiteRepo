using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SkoulouApp.WebApp.Migrations
{
    /// <inheritdoc />
    public partial class EineSchuleProKonto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentProfiles_AppUserId",
                table: "StudentProfiles");

            migrationBuilder.AddColumn<bool>(
                name: "IstPlattformZuordnung",
                table: "UserSchools",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Bestehende Betreuungen durch ProduktOwner sind Plattformzuordnungen und dürfen mehrfach bleiben.
            migrationBuilder.Sql("""
                UPDATE us SET IstPlattformZuordnung = 1
                FROM UserSchools us
                WHERE EXISTS (SELECT 1 FROM AspNetUserRoles ur
                              JOIN AspNetRoles r ON r.Id = ur.RoleId
                              WHERE ur.UserId = us.AppUserId AND r.Name = N'ProduktOwner');
                """);

            // Hat ein Schulkonto noch mehrere Schulen, schlägt dieser Index bewusst fehl (keine stille Datenänderung).
            // Vorher prüfen: SELECT AppUserId FROM UserSchools WHERE IstPlattformZuordnung = 0 GROUP BY AppUserId HAVING COUNT(*) > 1
            migrationBuilder.CreateIndex(
                name: "IX_UserSchools_EineSchuleProKonto",
                table: "UserSchools",
                column: "AppUserId",
                unique: true,
                filter: "[IstPlattformZuordnung] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_EinProfilProKonto",
                table: "StudentProfiles",
                column: "AppUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSchools_EineSchuleProKonto",
                table: "UserSchools");

            migrationBuilder.DropIndex(
                name: "IX_StudentProfiles_EinProfilProKonto",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "IstPlattformZuordnung",
                table: "UserSchools");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_AppUserId",
                table: "StudentProfiles",
                column: "AppUserId");
        }
    }
}
