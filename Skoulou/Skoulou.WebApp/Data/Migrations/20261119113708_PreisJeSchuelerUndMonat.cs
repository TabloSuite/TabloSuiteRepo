using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Skoulou.WebApp.Data.Migrations
{
    /// <summary>
    /// 19.11.2026 (Nutzerentscheidung): Preis je Schüler und Monat statt Jahrespreis ÷ Perioden. Spalten umbenennen (keine
    /// Datenverluste) und Werte umrechnen: Plattformpreis 3.600 → 500 (Standard), sonst ROUND(x / 10) (≈ 10 Monate je Schuljahr);
    /// abweichende Schulpreise und eingefrorene Jahrespreise ebenso ROUND(x / 10), mindestens 1. CGU-Version 2026-11-13 → 2026-11-19.
    /// Down spiegelbildlich: 500 → 3.600, sonst × 10, Spalten zurück.
    /// </summary>
    public partial class PreisJeSchuelerUndMonat : Migration
    {
        /// <summary>ROUND(x / 10) in ganzen FCFA, mindestens 1 (Preise sind immer ≥ 1).</summary>
        private static string Zehntel(string spalte)
            => $"CAST(CASE WHEN ROUND([{spalte}] / 10.0, 0) < 1 THEN 1 ELSE ROUND([{spalte}] / 10.0, 0) END AS bigint)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlattformJahrespreis",
                table: "Schuljahre",
                newName: "PlattformMonatspreis");

            migrationBuilder.RenameColumn(
                name: "JahrespreisOverride",
                table: "Schulen",
                newName: "MonatspreisOverride");

            migrationBuilder.RenameColumn(
                name: "JahrespreisProSchueler",
                table: "PlattformEinstellungen",
                newName: "MonatspreisProSchueler");

            // EXEC: erst zur Laufzeit übersetzt – in einem SQL-Skript (ein Batch) kennt der Compiler die umbenannten Spalten noch nicht.
            migrationBuilder.Sql(
                $"EXEC(N'UPDATE [PlattformEinstellungen] SET [MonatspreisProSchueler] = CASE WHEN [MonatspreisProSchueler] = 3600 THEN 500 ELSE {Zehntel("MonatspreisProSchueler")} END;');");
            migrationBuilder.Sql(
                $"EXEC(N'UPDATE [Schulen] SET [MonatspreisOverride] = {Zehntel("MonatspreisOverride")} WHERE [MonatspreisOverride] IS NOT NULL;');");
            migrationBuilder.Sql(
                $"EXEC(N'UPDATE [Schuljahre] SET [PlattformMonatspreis] = {Zehntel("PlattformMonatspreis")} WHERE [PlattformMonatspreis] IS NOT NULL;');");
            // CGU mit neuem Preismodell: neue Konten speichern bei der Registrierung die neue Version (keine erneute Zustimmung nötig).
            migrationBuilder.Sql("UPDATE [PlattformEinstellungen] SET [AgbVersion] = N'2026-11-19' WHERE [AgbVersion] = N'2026-11-13';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [PlattformEinstellungen] SET [AgbVersion] = N'2026-11-13' WHERE [AgbVersion] = N'2026-11-19';");
            migrationBuilder.Sql(
                "UPDATE [Schuljahre] SET [PlattformMonatspreis] = [PlattformMonatspreis] * 10 WHERE [PlattformMonatspreis] IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE [Schulen] SET [MonatspreisOverride] = [MonatspreisOverride] * 10 WHERE [MonatspreisOverride] IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE [PlattformEinstellungen] SET [MonatspreisProSchueler] = CASE WHEN [MonatspreisProSchueler] = 500 THEN 3600 ELSE [MonatspreisProSchueler] * 10 END;");

            migrationBuilder.RenameColumn(
                name: "PlattformMonatspreis",
                table: "Schuljahre",
                newName: "PlattformJahrespreis");

            migrationBuilder.RenameColumn(
                name: "MonatspreisOverride",
                table: "Schulen",
                newName: "JahrespreisOverride");

            migrationBuilder.RenameColumn(
                name: "MonatspreisProSchueler",
                table: "PlattformEinstellungen",
                newName: "JahrespreisProSchueler");
        }
    }
}
