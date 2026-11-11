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
}
