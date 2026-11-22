using System.Globalization;

namespace ENote.Core.Services;

/// <summary>
/// Einheitliche Darstellung für alle Server (lokal Deutsch, Azure Englisch): Französisch mit Punkt als
/// Tausendertrennzeichen – « 15.000 F », « −7,5 % », « 11/11/2026 », Wochentage und Monate auf Französisch.
/// Eingaben sind nicht betroffen (Zahlenfelder nutzen <c>type="number"</c> bzw. nur Ziffern).
/// </summary>
public static class EnoteKultur
{
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

    /// <summary>Unveränderliche Instanz für Texte außerhalb einer Anfrage (Mails, Banner, PDFs).</summary>
    public static CultureInfo Standard { get; } = CultureInfo.ReadOnly(Erstelle());

    /// <summary>
    /// Betrag wie überall in der App: « 38.880 F » (Punkt als Tausendertrenner, ohne Nachkommastellen). Seit 22.11.2026 auch in
    /// Mails und Tarif-Bannern (vorher « 38 880 F CFA » mit dem Leerzeichen von fr-FR; Nutzerentscheidung, Befund B-7).
    /// </summary>
    public static string Betrag(decimal betrag) => betrag.ToString("N0", Standard) + " F";
}
