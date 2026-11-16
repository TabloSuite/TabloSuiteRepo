namespace Skoulou.Core.Paedagogik;

/// <summary>
/// Sprachniveau (GER/CECRL) einer Audio-Aufgabe: bestimmt das Sprechtempo beim Vorlesen. Gespeichert ohne Schemaänderung
/// im Feld <c>Frage.Sprache</c>: « fr-FR » = Standard (B1, auch alle älteren Aufgaben), sonst « fr-FR/A2 »
/// (höchstens 8 der 10 Zeichen).
/// </summary>
public static class SprechNiveaus
{
    public const string Standard = "B1";
    public const char Trenner = '/';

    /// <summary>« Plus lentement » des Schülers: zusätzlich das 0,8-fache Tempo.</summary>
    public const double LangsamFaktor = 0.8;

    /// <summary>Langsamer liest Azure nicht (halbes Tempo).</summary>
    public const int MinProzent = -50;

    public const string Hinweis = "A1 : très lent · C2 : vitesse naturelle. L'élève peut encore ralentir.";

    /// <summary>Niveau, Bezeichnung und Tempo relativ zur natürlichen Stimme (Prozent, SSML « prosody rate »).</summary>
    public static readonly IReadOnlyList<(string Code, string Name, int Prozent)> Alle =
    [
        ("A1", "A1 · très lent", -35),
        ("A2", "A2 · lent", -25),
        ("B1", "B1 · posé", -15),
        ("B2", "B2 · presque naturel", -5),
        ("C1", "C1 · naturel", 0),
        ("C2", "C2 · naturel et vif", 5)
    ];

    /// <summary>Bekanntes Niveau in Großbuchstaben, sonst der Standard.</summary>
    public static string Normiert(string? niveau)
    {
        var n = niveau?.Trim().ToUpperInvariant();
        return Alle.Any(a => a.Code == n) ? n! : Standard;
    }

    /// <summary>Tempo in Prozent (0 = natürlich); mit « Plus lentement » langsamer, höchstens bis <see cref="MinProzent"/>.</summary>
    public static int Prozent(string? niveau, bool langsam)
    {
        var code = Normiert(niveau);
        var basis = Alle.First(a => a.Code == code).Prozent;
        if (!langsam) return basis;
        var verlangsamt = (int)Math.Round((100 + basis) * LangsamFaktor - 100, MidpointRounding.AwayFromZero);
        return Math.Max(MinProzent, verlangsamt);
    }

    /// <summary>Dasselbe Tempo als Faktor für die Browser-Stimme (speechSynthesis « rate », 1 = normal).</summary>
    public static double Faktor(string? niveau, bool langsam) => 1 + Prozent(niveau, langsam) / 100.0;

    /// <summary>Speicherform in <c>Frage.Sprache</c>: beim Standard nur die Sprache (ältere Aufgaben bleiben unverändert).</summary>
    public static string Verbinden(string sprache, string? niveau)
    {
        var n = Normiert(niveau);
        return n == Standard ? sprache : $"{sprache}{Trenner}{n}";
    }

    /// <summary>Zerlegt die Speicherform in Sprache und Niveau (ohne Angabe: Standard).</summary>
    public static (string Sprache, string Niveau) Trennen(string? gespeichert)
    {
        if (string.IsNullOrWhiteSpace(gespeichert)) return (FragenTypen.Sprachen[0].Code, Standard);
        var i = gespeichert.IndexOf(Trenner);
        return i < 0 ? (gespeichert.Trim(), Standard) : (gespeichert[..i].Trim(), Normiert(gespeichert[(i + 1)..]));
    }
}
