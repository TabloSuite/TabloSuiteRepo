using System.Globalization;

namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Feste Kultur der App: fr-FR mit Punkt als Tausendertrennzeichen und Komma als Dezimalzeichen
/// (« 150.000 FCFA », « 13,67 », « 12/11/2026 »). Unabhängig von Server und Browser.
/// </summary>
public static class SkoulouKultur
{
    private static readonly Lazy<CultureInfo> Instanz = new(Baue);

    /// <summary>Die gemeinsame, schreibgeschützte Kultur.</summary>
    public static CultureInfo Kultur => Instanz.Value;

    /// <summary>Erstellt die Kultur (für den Start und Tests).</summary>
    public static CultureInfo Erstelle() => Instanz.Value;

    private static CultureInfo Baue()
    {
        var kultur = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        var zahlen = kultur.NumberFormat;
        zahlen.NumberGroupSeparator = ".";
        zahlen.NumberDecimalSeparator = ",";
        zahlen.CurrencyGroupSeparator = ".";
        zahlen.CurrencyDecimalSeparator = ",";
        zahlen.PercentGroupSeparator = ".";
        zahlen.PercentDecimalSeparator = ",";
        zahlen.CurrencySymbol = "FCFA";
        kultur.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        return CultureInfo.ReadOnly(kultur);
    }

    /// <summary>Betrag in ganzen FCFA: « 150.000 FCFA ».</summary>
    public static string Fcfa(long betrag) => betrag.ToString("N0", Kultur) + " FCFA";

    /// <summary>Ganze Zahl mit Tausenderpunkt: « 1.250 ».</summary>
    public static string Zahl(long wert) => wert.ToString("N0", Kultur);

    /// <summary>Dezimalzahl mit fester Stellenzahl: « 13,67 ».</summary>
    public static string Zahl(decimal wert, int stellen = 2) => wert.ToString("N" + stellen, Kultur);

    /// <summary>Datum kurz: « 12/11/2026 ».</summary>
    public static string Datum(DateOnly datum) => datum.ToString("dd/MM/yyyy", Kultur);

    /// <summary>Datum lang: « jeudi 12 novembre 2026 ».</summary>
    public static string DatumLang(DateOnly datum) => datum.ToString("dddd d MMMM yyyy", Kultur);

    /// <summary>Uhrzeit: « 08:30 ».</summary>
    public static string Uhrzeit(TimeOnly zeit) => zeit.ToString("HH:mm", Kultur);
}
