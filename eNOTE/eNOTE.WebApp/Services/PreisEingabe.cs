namespace ENote.WebApp.Services;

/// <summary>
/// Preise im Katalog sind ganze Francs CFA (keine Centimes). Das Zahlenfeld des Browsers liest « 1.500 » als 1,5 –
/// im Klicktest 20.11.2026 entstand so ein Artikel zu 1,50 F, angezeigt als « 2 F ». Statt still zu runden: Meldung.
/// </summary>
public static class PreisEingabe
{
    public const string MeldungGanzeFrancs = "Les prix se saisissent en francs entiers, sans point ni virgule (ex. 1500 pour 1.500 F).";

    /// <summary>Null = in Ordnung, sonst die französische Meldung.</summary>
    public static string? Pruefe(decimal preis, decimal? kaufpreis = null)
        => preis != decimal.Truncate(preis) || kaufpreis is decimal k && k != decimal.Truncate(k) ? MeldungGanzeFrancs : null;
}
