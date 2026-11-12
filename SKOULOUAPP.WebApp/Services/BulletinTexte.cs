using System.Globalization;
using SkoulouApp.Core.DataModels.Unterricht;

namespace SkoulouApp.WebApp.Services;

/// <summary>Texte und Regeln des allgemeinen Bulletins (Muster aus Côte d'Ivoire und Togo zusammengeführt).</summary>
public static class BulletinTexte
{
    private static readonly CultureInfo Fr = SkoulouApp.Core.Services.SkoulouKultur.Instanz;

    /// <summary>Appréciation nach üblicher Notenskala (/20), falls die Lehrkraft keine eigene eingetragen hat.</summary>
    public static string? Appreciation(decimal? note) => note switch
    {
        null => null,
        >= 18m => "Excellent",
        >= 16m => "Très bien",
        >= 14m => "Bien",
        >= 12m => "Assez bien",
        >= 10m => "Passable",
        >= 8m => "Insuffisant",
        >= 5m => "Faible",
        _ => "Très faible"
    };

    /// <summary>Vorschlag des Klassenrats aus der Periodenmoyenne (ohne eigenen Eintrag).</summary>
    public static KlassenratDistinktion DistinktionVorschlag(decimal? moyenne) => moyenne switch
    {
        >= 16m => KlassenratDistinktion.Felicitations,
        >= 14m => KlassenratDistinktion.Encouragements,
        >= 12m => KlassenratDistinktion.TableauHonneur,
        _ => KlassenratDistinktion.Keine
    };

    public static string? DistinktionText(KlassenratDistinktion distinktion) => distinktion switch
    {
        KlassenratDistinktion.TableauHonneur => "Tableau d'honneur",
        KlassenratDistinktion.Encouragements => "Tableau d'honneur + Encouragements",
        KlassenratDistinktion.Felicitations => "Tableau d'honneur + Félicitations",
        _ => null
    };

    public static readonly (KlassenratSanktion Wert, string Text)[] Sanktionen =
    [
        (KlassenratSanktion.AvertissementTravail, "Avertissement travail"),
        (KlassenratSanktion.AvertissementConduite, "Avertissement conduite"),
        (KlassenratSanktion.BlameTravail, "Blâme travail"),
        (KlassenratSanktion.BlameConduite, "Blâme conduite")
    ];

    public static List<string> SanktionTexte(KlassenratSanktion sanktionen)
        => Sanktionen.Where(s => sanktionen.HasFlag(s.Wert)).Select(s => s.Text).ToList();

    public static string? EntscheidungText(Jahresentscheidung entscheidung) => entscheidung switch
    {
        Jahresentscheidung.Admis => "Admis(e) en classe supérieure",
        Jahresentscheidung.Redouble => "Redouble la classe",
        Jahresentscheidung.Exclu => "Exclu(e)",
        _ => null
    };

    /// <summary>1 → « 1er », 2 → « 2e » …</summary>
    public static string Rang(int? rang) => rang switch { null => "—", 1 => "1er", _ => $"{rang}e" };

    public static string Note(decimal? wert)
        => wert is null ? "—" : Core.Services.NotenBerechnung.Anzeige(wert.Value).ToString("0.00", Fr);

    /// <summary>Moyenne in Worten, z. B. 11,02 → « onze virgule zéro deux ».</summary>
    public static string InWorten(decimal? wert)
    {
        if (wert is null) return "—";
        var gerundet = Core.Services.NotenBerechnung.Anzeige(wert.Value);
        var ganz = (int)Math.Floor(gerundet);
        var hundertstel = (int)Math.Round((gerundet - ganz) * 100m);
        var text = Zahl(ganz);
        if (hundertstel == 0) return text;
        var nachkomma = hundertstel < 10 ? "zéro " + Zahl(hundertstel) : Zahl(hundertstel);
        return $"{text} virgule {nachkomma}";
    }

    private static readonly string[] Einer = ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"];
    private static readonly string[] Zehner = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];

    private static string Zahl(int n)
    {
        if (n < 20) return Einer[n];
        if (n >= 100) return n.ToString(Fr);
        var z = n / 10;
        var e = n % 10;
        if (z is 7 or 9) e += 10;
        var basis = Zehner[z];
        if (e == 0) return z == 8 ? "quatre-vingts" : basis;
        var verbindung = e is 1 or 11 && z != 8 && z != 9 ? " et " : "-";
        return basis + verbindung + Einer[e];
    }
}
