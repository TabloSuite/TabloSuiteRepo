using System.Text.RegularExpressions;

namespace ENote.WebApp.Services.KiHilfe;

/// <summary>Art eines Antwortblocks.</summary>
public enum KiBlockArt { Absatz, Schritte, Punkte }

/// <summary>Ein Block der Antwort (Absatz, nummerierte Schritte oder Aufzählung) – immer als Text angezeigt, nie als HTML.</summary>
public sealed record KiBlock(KiBlockArt Art, IReadOnlyList<string> Zeilen);

/// <summary>Zerlegt die Antwort in Blöcke; Markdown-Reste (**, #) werden entfernt.</summary>
public static partial class KiAntwortFormat
{
    public static IReadOnlyList<KiBlock> Bloecke(string? text)
    {
        var bloecke = new List<KiBlock>();
        KiBlockArt? art = null;
        var zeilen = new List<string>();

        void Abschliessen()
        {
            if (art is { } a && zeilen.Count > 0) bloecke.Add(new KiBlock(a, [.. zeilen]));
            zeilen.Clear();
            art = null;
        }

        foreach (var roh in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
        {
            var zeile = Bereinige(roh);
            if (zeile.Length == 0) { Abschliessen(); continue; }
            var (neu, inhalt) = Schritt().Match(zeile) is { Success: true } s ? (KiBlockArt.Schritte, s.Groups[1].Value)
                : Punkt().Match(zeile) is { Success: true } p ? (KiBlockArt.Punkte, p.Groups[1].Value)
                : (KiBlockArt.Absatz, zeile);
            if (art != neu) Abschliessen();
            art = neu;
            zeilen.Add(inhalt.Trim());
        }
        Abschliessen();
        return bloecke;
    }

    private static string Bereinige(string zeile)
    {
        var z = zeile.Replace("**", string.Empty).Replace("__", string.Empty).Trim();
        return Ueberschrift().Replace(z, string.Empty).Trim();
    }

    [GeneratedRegex(@"^\d{1,2}[.)]\s+(.+)$")]
    private static partial Regex Schritt();

    [GeneratedRegex(@"^[-•*]\s+(.+)$")]
    private static partial Regex Punkt();

    [GeneratedRegex(@"^#{1,6}\s*")]
    private static partial Regex Ueberschrift();
}
