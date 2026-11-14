namespace Skoulou.Core.Werkzeuge;

/// <summary>
/// Ganze Zahl in französischen Worten (traditionelle Schreibweise, bis 999.999.999):
/// « soixante et onze », « quatre-vingts », « quatre-vingt mille », « deux cents », « deux cent mille », « mille un ».
/// </summary>
public static class ZahlInWorten
{
    public const long Maximum = 999_999_999;

    private static readonly string[] Einer =
    [
        "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"
    ];

    private static readonly string[] Zehner = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante"];

    /// <summary>Betrag mit Währung: « vingt-cinq mille francs CFA ».</summary>
    public static string Fcfa(long betrag) => $"{Text(betrag)} francs CFA";

    /// <summary>Zahl in Worten: « un million deux cent cinquante mille ».</summary>
    public static string Text(long zahl)
    {
        if (zahl < 0) return "moins " + Text(-zahl);
        if (zahl > Maximum) throw new ArgumentOutOfRangeException(nameof(zahl), "Montant trop élevé pour être écrit en lettres.");
        if (zahl == 0) return Einer[0];

        var teile = new List<string>(3);
        var millionen = (int)(zahl / 1_000_000);
        var tausender = (int)(zahl / 1_000 % 1_000);
        var rest = (int)(zahl % 1_000);

        // « million » ist ein Nomen: « deux cents millions », « quatre-vingts millions » (Plural-s bleibt).
        if (millionen > 0) teile.Add(Unter1000(millionen, amEnde: true) + (millionen > 1 ? " millions" : " million"));
        // « mille » ist ein Zahladjektiv: « deux cent mille », « quatre-vingt mille », nie « un mille ».
        if (tausender == 1) teile.Add("mille");
        else if (tausender > 1) teile.Add(Unter1000(tausender, amEnde: false) + " mille");
        if (rest > 0) teile.Add(Unter1000(rest, amEnde: true));
        return string.Join(' ', teile);
    }

    private static string Unter1000(int zahl, bool amEnde)
    {
        var hunderter = zahl / 100;
        var rest = zahl % 100;
        var text = hunderter switch
        {
            0 => string.Empty,
            1 => "cent",
            _ => Einer[hunderter] + " cent" + (rest == 0 && amEnde ? "s" : string.Empty)
        };
        if (rest == 0) return text;
        var unter100 = Unter100(rest, amEnde);
        return text.Length == 0 ? unter100 : text + " " + unter100;
    }

    private static string Unter100(int zahl, bool amEnde)
    {
        if (zahl < 20) return Einer[zahl];
        if (zahl < 70)
        {
            var einer = zahl % 10;
            var zehner = Zehner[zahl / 10];
            return einer switch { 0 => zehner, 1 => zehner + " et un", _ => zehner + "-" + Einer[einer] };
        }
        if (zahl < 80) return zahl == 71 ? "soixante et onze" : "soixante-" + Einer[zahl - 60];
        return zahl == 80 ? "quatre-vingt" + (amEnde ? "s" : string.Empty) : "quatre-vingt-" + Einer[zahl - 80];
    }
}
