using System.Globalization;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Einheitliche Darstellung auf jedem Server (lokal Deutsch, Azure Englisch) wie bei eNOTE: Französisch mit Punkt
/// als Tausendertrennzeichen – « 150.000 FCFA », « 13,67 », « 12/11/2026 », Wochentage und Monate auf Französisch.
/// Wird in Program.cs als Standardkultur gesetzt; Zahlenfelder (<c>type="number"</c>) sind davon nicht betroffen.
/// </summary>
public static class SkoulouKultur
{
    /// <summary>Gemeinsame, schreibgeschützte Instanz.</summary>
    public static CultureInfo Instanz { get; } = CultureInfo.ReadOnly(Erstelle());

    public static CultureInfo Erstelle()
    {
        var kultur = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        var zahlen = kultur.NumberFormat;
        zahlen.NumberGroupSeparator = ".";
        zahlen.CurrencyGroupSeparator = ".";
        zahlen.PercentGroupSeparator = ".";
        zahlen.NumberDecimalSeparator = ",";
        zahlen.CurrencyDecimalSeparator = ",";
        zahlen.PercentDecimalSeparator = ",";
        return kultur;
    }
}
