namespace ENote.WebApp.Services;

/// <summary>
/// Preise im Katalog sind ganze Francs CFA (keine Centimes). Das Zahlenfeld des Browsers liest « 1.500 » als 1,5 –
/// im Klicktest 20.11.2026 entstand so ein Artikel zu 1,50 F, angezeigt als « 2 F ». Statt still zu runden: Meldung.
/// </summary>
public static class PreisEingabe
{
    /// <summary>Leeres oder unlesbares Preisfeld (Fehlersuche 21.11.2026: wurde still als 0 F gespeichert).</summary>
    public const string MeldungPreisFehlt = "Veuillez saisir le prix en francs (0 pour une prestation gratuite).";

    public const string MeldungGanzeFrancs ="Les prix se saisissent en francs entiers, sans point ni virgule (ex. 1500 pour 1.500 F).";

    /// <summary>Null = in Ordnung, sonst die französische Meldung.</summary>
    public static string? Pruefe(decimal preis, decimal? kaufpreis = null)
        => preis != decimal.Truncate(preis) || kaufpreis is decimal k && k != decimal.Truncate(k) ? MeldungGanzeFrancs : null;
}
