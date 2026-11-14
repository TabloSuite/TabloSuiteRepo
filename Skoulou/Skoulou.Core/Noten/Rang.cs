namespace Skoulou.Core.Noten;

/// <summary>Rang eines Schülers: Wettbewerbsrang (« ex » bei Gleichstand) oder « Non classé » (Rang null).</summary>
public readonly record struct RangInfo(int? Rang, bool Ex)
{
    public static readonly RangInfo NonClasse = new(null, false);

    public bool Klassiert => Rang.HasValue;

    /// <summary>« 1er », « 3e ex », « Non classé ».</summary>
    public string Text => NotenTexte.Rang(this);
}

/// <summary>
/// Eine Rangregel überall (Bauplan §5 Regel 5): Wettbewerbsrang über die gerundeten Werte der klassierten Schüler;
/// Gleichstand = gleicher Rang mit « ex », der nächste Rang wird übersprungen (1, 1, 3); ohne Wert « Non classé ».
/// </summary>
public static class RangRechner
{
    public static IReadOnlyDictionary<TSchluessel, RangInfo> Berechne<TSchluessel>(IEnumerable<(TSchluessel Schluessel, decimal? Wert)> werte)
        where TSchluessel : notnull
    {
        var ergebnis = new Dictionary<TSchluessel, RangInfo>();
        var klassiert = new List<(TSchluessel Schluessel, decimal Wert)>();
        foreach (var (schluessel, wert) in werte)
        {
            if (wert is { } w) klassiert.Add((schluessel, NotenRechner.Runde(w)));
            else ergebnis[schluessel] = RangInfo.NonClasse;
        }
        klassiert.Sort((a, b) => b.Wert.CompareTo(a.Wert));
        var i = 0;
        while (i < klassiert.Count)
        {
            var j = i;
            while (j < klassiert.Count && klassiert[j].Wert == klassiert[i].Wert) j++;
            for (var k = i; k < j; k++) ergebnis[klassiert[k].Schluessel] = new RangInfo(i + 1, j - i > 1);
            i = j;
        }
        return ergebnis;
    }

    /// <summary>Ränge aus Mittelwerten (nur Werte zählen; unvollständig/nicht bewertet = « Non classé »).</summary>
    public static IReadOnlyDictionary<TSchluessel, RangInfo> AusMitteln<TSchluessel>(IEnumerable<(TSchluessel Schluessel, Mittel Mittel)> werte)
        where TSchluessel : notnull
        => Berechne(werte.Select(w => (w.Schluessel, w.Mittel.HatWert ? w.Mittel.Wert : null)));
}

/// <summary>Klassenstatistik über die klassierten Schüler (gerundete Werte): Mittel, Min, Max, Anteil ≥ 10.</summary>
public sealed record KlassenStatistik(int Effectif, int Klassiert, decimal? Mittel, decimal? Min, decimal? Max, decimal? ProzentUeber10)
{
    public static KlassenStatistik Berechne(IReadOnlyCollection<Mittel> werte)
    {
        var gerundet = werte.Where(w => w.HatWert).Select(w => w.Gerundet!.Value).ToList();
        if (gerundet.Count == 0) return new KlassenStatistik(werte.Count, 0, null, null, null, null);
        var ueber10 = gerundet.Count(w => w >= 10);
        return new KlassenStatistik(werte.Count, gerundet.Count, NotenRechner.Runde(gerundet.Average()), gerundet.Min(), gerundet.Max(),
            NotenRechner.Runde(ueber10 * 100m / gerundet.Count));
    }
}
