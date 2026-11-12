using ENote.Core.Services.Kunden;

namespace ENote.Core.Services.Kasse;

/// <summary>Regeln für Tickets « en attente » (zwischengespeicherte, noch nicht kassierte Tickets).</summary>
public static class TicketParkRegeln
{
    public const int NotizMax = 120;
    public const int PositionenMax = 100;
    public const int MengeMax = 999;

    /// <summary>Notiz bereinigen (eine Zeile, max. <see cref="NotizMax"/> Zeichen); leer → null.</summary>
    public static string? PruefeNotiz(string? notiz)
    {
        var n = KundenBesuchRegeln.Bereinige(notiz).Replace('\n', ' ');
        if (n.Length > NotizMax) throw new ArgumentException($"Note : {NotizMax} caractères au maximum.");
        return n.Length == 0 ? null : n;
    }

    /// <summary>
    /// Leistungen, die zum Anlass eines anwesenden Kunden passen (« Présent pour Tresses africaines »):
    /// ganzer Anlass oder seine Teile (getrennt durch « , », « + », « / », « et »), Name exakt ohne Groß-/Kleinschreibung und Akzente.
    /// </summary>
    public static IReadOnlyList<T> PassendZumAnlass<T>(string? anlass, IEnumerable<T> leistungen, Func<T, string> name)
    {
        if (string.IsNullOrWhiteSpace(anlass)) return [];
        var liste = leistungen.ToList();
        // Ganzer Anlass ist eine Leistung (auch « Coupe + barbe ») → nur diese
        var ganz = liste.FirstOrDefault(x => Schluessel(name(x)) == Schluessel(anlass));
        if (ganz is not null) return [ganz];
        var treffer = new List<T>();
        var teile = System.Text.RegularExpressions.Regex.Split(anlass, @"\s*(?:,|\+|/|\bet\b)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var teil in teile.Select(Schluessel).Where(t => t.Length > 0).Distinct())
        {
            var l = liste.FirstOrDefault(x => Schluessel(name(x)) == teil);
            if (l is not null && !treffer.Contains(l)) treffer.Add(l);
        }
        return treffer;
    }

    private static string Schluessel(string text)
    {
        var normal = KundenBesuchRegeln.Bereinige(text).Normalize(System.Text.NormalizationForm.FormD);
        var ohne = new string(normal.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return ohne.ToLowerInvariant();
    }
}
