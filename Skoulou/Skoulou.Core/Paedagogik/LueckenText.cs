using System.Text.RegularExpressions;

namespace Skoulou.Core.Paedagogik;

/// <summary>Teil eines Lückentextes: entweder HTML-Text oder eine Lücke (Nr ab 1).</summary>
public sealed record LueckenTeil(string? Html, int? Nr);

/// <summary>
/// Lückentexte mit Markern <c>[[1]]</c>, <c>[[2]]</c> …: Prüfung beim Speichern (jede Lücke genau
/// einmal, lückenlos nummeriert, passend zur Zahl der definierten Lücken) und Zerlegung zur Anzeige.
/// </summary>
public static partial class LueckenText
{
    public const int MaxLuecken = 30;

    /// <summary>
    /// Trennt mehrere akzeptierte Antworten einer Lücke im Editor. Früher « / » – damit war ein Bruch wie « 6/7 » als
    /// Antwort unmöglich (Normaltest 21.11.2026, Nutzerentscheidung 22.11.). Gespeichert wird ohnehin eine Liste.
    /// </summary>
    public const char AntwortTrenner = '|';

    /// <summary>Eingabe « Paris | Lutèce » → [« Paris », « Lutèce »]; « 6/7 » bleibt eine Antwort.</summary>
    public static List<string> Antworten(string? eingabe)
        => (eingabe ?? string.Empty).Split(AntwortTrenner, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Akzeptierte Antworten einer Lücke zum Bearbeiten und Anzeigen (« Paris | Lutèce »).</summary>
    public static string AlsText(IEnumerable<string>? antworten) => string.Join($" {AntwortTrenner} ", antworten ?? []);

    /// <summary>Gefundene Marker in Textreihenfolge.</summary>
    public static IReadOnlyList<int> Marker(string? text)
        => string.IsNullOrEmpty(text) ? [] : MarkerRegex().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).ToList();

    /// <summary>Fehlermeldung oder null, wenn Text und Lücken zusammenpassen.</summary>
    public static string? Pruefe(string? text, int anzahlLuecken)
    {
        var marker = Marker(text);
        if (marker.Count == 0) return "Le texte doit contenir les trous sous la forme [[1]], [[2]]…";
        if (marker.Count > MaxLuecken) return $"Un texte à trous peut contenir au plus {MaxLuecken} trous.";
        var doppelt = marker.GroupBy(n => n).FirstOrDefault(g => g.Count() > 1);
        if (doppelt is not null) return $"Le trou [[{doppelt.Key}]] apparaît plusieurs fois.";
        if (marker.Order().Where((n, i) => n != i + 1).Any())
            return $"Les trous doivent être numérotés de [[1]] à [[{marker.Count}]], sans en sauter.";
        if (marker.Count != anzahlLuecken)
            return $"Le texte contient {marker.Count} trou(s), mais {anzahlLuecken} réponse(s) sont définies.";
        return null;
    }

    /// <summary>Zerlegt (sicheres, bereits bereinigtes) HTML in Text- und Lückenteile.</summary>
    public static IReadOnlyList<LueckenTeil> Zerlege(string? html)
    {
        var teile = new List<LueckenTeil>();
        if (string.IsNullOrEmpty(html)) return teile;
        var pos = 0;
        foreach (Match m in MarkerRegex().Matches(html))
        {
            if (m.Index > pos) teile.Add(new LueckenTeil(html[pos..m.Index], null));
            teile.Add(new LueckenTeil(null, int.Parse(m.Groups[1].Value)));
            pos = m.Index + m.Length;
        }
        if (pos < html.Length) teile.Add(new LueckenTeil(html[pos..], null));
        return teile;
    }

    /// <summary>Text mit « ____ » statt Markern (Druck, Vorschau).</summary>
    public static string MitStrichen(string? html, string strich = "________")
        => string.IsNullOrEmpty(html) ? string.Empty : MarkerRegex().Replace(html, strich);

    [GeneratedRegex(@"\[\[\s*(\d{1,3})\s*\]\]")]
    private static partial Regex MarkerRegex();
}
