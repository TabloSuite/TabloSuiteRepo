namespace Skoulou.Core.Noten;

/// <summary>
/// Moyenne « en toutes lettres » für das Bulletin (gerundeter Wert, 0 … 99,99): 11,02 → « onze virgule zéro deux »,
/// 14,5 → « quatorze virgule cinquante », 8 → « huit ». (Geldbeträge in Worten: siehe Bereich Finanzen.)
/// </summary>
public static class NoteInWorten
{
    private static readonly string[] Einer =
    [
        "zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"
    ];

    private static readonly string[] Zehner = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante"];

    public static string Text(decimal wert)
    {
        var gerundet = NotenRechner.Runde(Math.Abs(wert));
        if (gerundet >= 100) throw new ArgumentOutOfRangeException(nameof(wert), "Valeur hors plage (0 à 99,99).");
        var ganz = (int)Math.Truncate(gerundet);
        var dezimal = (int)((gerundet - ganz) * 100);
        var text = Zahl(ganz);
        if (dezimal == 0) return text;
        return text + " virgule " + (dezimal < 10 ? "zéro " + Zahl(dezimal) : Zahl(dezimal));
    }

    /// <summary>Ganze Zahl 0 … 99 in Worten (französische Schreibweise mit Bindestrichen).</summary>
    public static string Zahl(int n)
    {
        if (n is < 0 or > 99) throw new ArgumentOutOfRangeException(nameof(n));
        if (n < 20) return Einer[n];
        if (n < 70)
        {
            var (z, e) = (n / 10, n % 10);
            return e switch { 0 => Zehner[z], 1 => Zehner[z] + " et un", _ => Zehner[z] + "-" + Einer[e] };
        }
        if (n < 80) return n == 71 ? "soixante et onze" : "soixante-" + Einer[n - 60];
        if (n == 80) return "quatre-vingts";
        return "quatre-vingt-" + Einer[n - 80];
    }
}
