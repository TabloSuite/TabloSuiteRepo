using Skoulou.Core.Entitaeten;

namespace Skoulou.Tests.Simulation;

/// <summary>Deterministischer Zufall (fester Startwert je Schule/Phase) mit kleinen Helfern.</summary>
internal sealed class Zufall(int startwert)
{
    private readonly Random r = new(startwert);

    public int Zahl(int min, int maxExklusiv) => r.Next(min, maxExklusiv);
    public double Anteil() => r.NextDouble();
    public bool Ja(double wahrscheinlichkeit) => r.NextDouble() < wahrscheinlichkeit;
    public T Eins<T>(IReadOnlyList<T> liste) => liste[r.Next(liste.Count)];

    /// <summary>Normalverteilt (Box-Muller).</summary>
    public double Normal(double mittel, double streuung)
    {
        var u1 = 1.0 - r.NextDouble();
        var u2 = r.NextDouble();
        return mittel + streuung * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    public List<T> Mische<T>(IEnumerable<T> quelle)
    {
        var l = quelle.ToList();
        for (var i = l.Count - 1; i > 0; i--)
        {
            var j = r.Next(i + 1);
            (l[i], l[j]) = (l[j], l[i]);
        }
        return l;
    }

    public static int Kombiniere(params int[] teile)
    {
        unchecked
        {
            var h = 17;
            foreach (var t in teile) h = h * 31 + t;
            return h & 0x7FFFFFFF;
        }
    }
}

/// <summary>Fiktive französisch-togolesische Namen, Orte und Schulen.</summary>
internal static class SimNamen
{
    public static readonly string[] Jungen =
    [
        "Kodjo", "Komla", "Kossi", "Koffi", "Yao", "Kwami", "Mawuli", "Sena", "Edem", "Elom", "Dodji", "Selom", "Yawo", "Kokou",
        "Agbéko", "Essowè", "Pyabalo", "Tchilabalo", "Kpatcha", "Essossimna", "Bilali", "Abalo", "Pierre", "Jean", "Paul",
        "Emmanuel", "Joël", "Romaric", "Florent", "Armand", "Didier", "Gérard", "Hervé", "Ibrahim", "Moussa", "Rachid", "Samuel",
        "Daniel", "Prince", "Fabrice", "Wilfried", "Arnaud", "Gaëtan", "Jérôme", "Kévin", "Rodrigue", "Éric", "Séraphin",
        "Aimé", "Désiré", "Honoré", "Innocent", "Léandre", "Mathieu", "Noël", "Rémi", "Thierry", "Ulrich", "Victorien", "Yannick"
    ];

    public static readonly string[] Maedchen =
    [
        "Akossiwa", "Afi", "Ama", "Abla", "Adjoa", "Akouvi", "Ayaba", "Mawuena", "Enyonam", "Sika", "Dzigbodi", "Essi", "Yawa",
        "Kafui", "Eyram", "Delali", "Sitsofe", "Elinam", "Nadège", "Mireille", "Pélagie", "Bernadette", "Clarisse", "Estelle",
        "Félicité", "Grâce", "Hélène", "Joséphine", "Léontine", "Marie", "Nathalie", "Océane", "Pascaline", "Rébecca", "Sandrine",
        "Thérèse", "Victoire", "Yvette", "Zoé", "Aïcha", "Fatima", "Mariam", "Rachida", "Solange", "Béatrice", "Célestine",
        "Edwige", "Françoise", "Gisèle", "Irène", "Agnès", "Brigitte", "Chantal", "Dorothée", "Éléonore", "Huguette", "Inès"
    ];

    public static readonly string[] Nachnamen =
    [
        "Agbeko", "Amegah", "Adjovi", "Akakpo", "Kpodar", "Mensah", "Lawson", "Ayité", "Dossou", "Gbeasor", "Koudawo", "Tchalla",
        "Tchédré", "Kpéli", "Afandalo", "Bawa", "Dermane", "Essowa", "Hounkpati", "Kodjovi", "Kokoroko", "Kuevidjen", "Lokossou",
        "Messan", "Noussoukpoe", "Pidalatan", "Sossou", "Tété", "Wilson", "d'Almeida", "de Souza", "Ahadji", "Akpabie", "Alognon",
        "Apedo", "Atayi", "Attiogbe", "Azianu", "Dogbe", "Djossou", "Edorh", "Fiagan", "Gbadoe", "Hodo", "Johnson", "Kuassi",
        "Klutse", "Kugbe", "Mawussi", "Nyaku", "Quashie", "Sedzro", "Segbedji", "Tossou", "Zinsou", "Badjo", "Batcha", "Bouraïma",
        "Djobo", "Issifou", "Labité", "Moumouni", "Nabine", "Ouro-Agoro", "Samah", "Tagba", "Tchakei", "Tchamdja", "Wiyao",
        "Yacoubou", "Assih", "Bakali", "Bamazi", "Dadja", "Essiomle", "Kanazoe", "Laré", "Pali", "Tchao", "Adjamagbo", "Agbodjan",
        "Amouzou", "Anani", "Bassowa", "Dzidzinyo", "Folly", "Gaba", "Komlan", "Logossou", "Mivedor", "Nomenyo", "Sodji", "Vovor"
    ];

    public static readonly string[] Geburtsorte = ["Lomé", "Kpalimé", "Aného", "Tsévié", "Kara", "Sokodé", "Atakpamé", "Dapaong", "Notsé", "Bassar", "Vogan", "Badou"];

    public static (string Vorname, Geschlecht Geschlecht) Vorname(Zufall z)
        => z.Ja(0.5) ? (z.Eins(Maedchen), Geschlecht.Weiblich) : (z.Eins(Jungen), Geschlecht.Maennlich);

    public static string Nachname(Zufall z) => z.Eins(Nachnamen);

    public static string Telefon(Zufall z) => $"+228 {z.Eins(new[] { "90", "91", "92", "93", "70", "79", "96", "97", "98", "99" })} {z.Zahl(10, 100)} {z.Zahl(10, 100)} {z.Zahl(10, 100)}";

    public static string Mail(string vorname, string nachname, int nr)
        => $"{Ascii(vorname)}.{Ascii(nachname)}{nr}@exemple-sim.tg";

    /// <summary>Kleinbuchstaben ohne Akzente (für Adressen).</summary>
    public static string Ascii(string text)
    {
        var normal = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normal)
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
