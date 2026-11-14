namespace Skoulou.Core.Noten;

/// <summary>Fach einer Klasse mit Koeffizient.</summary>
public sealed record FachSpalte(Guid FachId, decimal Koeffizient);

/// <summary>Wertungen eines Schülers je Fach (fehlendes Fach = keine Bewertung).</summary>
public sealed record SchuelerWertungen(Guid Id, IReadOnlyDictionary<Guid, IReadOnlyList<Wertung>> JeFach);

public sealed record FachErgebnis(Mittel Mittel, RangInfo Rang);

public sealed record SchuelerErgebnis(Guid Id, IReadOnlyDictionary<Guid, FachErgebnis> Faecher, Mittel Moyenne, RangInfo Rang);

/// <summary>Ergebnis einer Klasse in einer Periode: je Schüler Fachmittel, Moyenne, Ränge; Statistik gesamt und je Fach.</summary>
public sealed record KlassenErgebnis(IReadOnlyList<SchuelerErgebnis> Schueler, KlassenStatistik Statistik, IReadOnlyDictionary<Guid, KlassenStatistik> JeFach)
{
    public SchuelerErgebnis? Fuer(Guid id) => Schueler.FirstOrDefault(s => s.Id == id);
}

/// <summary>Jahresbilanz eines Schülers.</summary>
public sealed record JahresErgebnis(Guid Id, Mittel Moyenne, RangInfo Rang, IReadOnlyDictionary<Guid, Mittel> JeFach);

/// <summary>
/// Rechnet eine ganze Klasse in einem Durchgang (Daten einmal laden, keine N²-Abfragen – Fachkonzept 04 §7.4):
/// Fachmittel (Regel 1), Moyenne (Regel 2), Ränge über die Kohorte (Regel 5), Statistik.
/// </summary>
public static class KlassenRechner
{
    public static KlassenErgebnis Berechne(IReadOnlyList<FachSpalte> faecher, IReadOnlyList<SchuelerWertungen> schueler, bool absentZaehltNull)
    {
        var fachMittel = schueler.ToDictionary(s => s.Id, s => faecher.ToDictionary(f => f.FachId,
            f => s.JeFach.TryGetValue(f.FachId, out var w) ? NotenRechner.Fachmittel(w, absentZaehltNull) : Mittel.NonEvalue));
        var moyennes = schueler.ToDictionary(s => s.Id, s => NotenRechner.MoyenneGenerale(faecher.Select(f => (fachMittel[s.Id][f.FachId], f.Koeffizient))));
        var raenge = RangRechner.AusMitteln(moyennes.Select(m => (m.Key, m.Value)));
        var fachRaenge = faecher.ToDictionary(f => f.FachId, f => RangRechner.AusMitteln(schueler.Select(s => (s.Id, fachMittel[s.Id][f.FachId]))));

        var ergebnisse = schueler.Select(s => new SchuelerErgebnis(s.Id,
                faecher.ToDictionary(f => f.FachId, f => new FachErgebnis(fachMittel[s.Id][f.FachId], fachRaenge[f.FachId][s.Id])),
                moyennes[s.Id], raenge[s.Id]))
            .ToList();
        var jeFach = faecher.ToDictionary(f => f.FachId, f => KlassenStatistik.Berechne(schueler.Select(s => fachMittel[s.Id][f.FachId]).ToList()));
        return new KlassenErgebnis(ergebnisse, KlassenStatistik.Berechne(moyennes.Values.ToList()), jeFach);
    }

    /// <summary>
    /// « Bilan de fin d'année »: Jahresmittel (Regel 3) und Fach-Jahresmittel je Schüler aus den Periodenergebnissen,
    /// Jahresrang über die Kohorte. Perioden: (Ergebnis, Jahresgewicht, zählt fürs Jahr).
    /// </summary>
    public static IReadOnlyDictionary<Guid, JahresErgebnis> Bilan(IReadOnlyList<Guid> kohorte, IReadOnlyList<Guid> fachIds,
        IReadOnlyList<(KlassenErgebnis Ergebnis, decimal Gewicht, bool ZaehltFuerJahr)> perioden)
    {
        var jahr = kohorte.ToDictionary(id => id, id => NotenRechner.Jahresmittel(perioden.Select(p =>
            (p.Ergebnis.Fuer(id)?.Moyenne ?? Mittel.NonEvalue, p.Gewicht, p.ZaehltFuerJahr))));
        var raenge = RangRechner.AusMitteln(jahr.Select(j => (j.Key, j.Value)));
        return kohorte.ToDictionary(id => id, id => new JahresErgebnis(id, jahr[id], raenge[id],
            fachIds.ToDictionary(f => f, f => NotenRechner.FachJahresmittel(perioden.Select(p =>
                (p.Ergebnis.Fuer(id)?.Faecher.GetValueOrDefault(f)?.Mittel ?? Mittel.NonEvalue, p.Gewicht, p.ZaehltFuerJahr))))));
    }

    /// <summary>Fachgruppen-Bilan (Lettres, Sciences …): Σ(Moy·Coef)/ΣCoef über Fächer mit Wert.</summary>
    public static Mittel GruppenMittel(IEnumerable<(Mittel Mittel, decimal Koeffizient)> faecher)
        => NotenRechner.MoyenneGenerale(faecher.Where(f => f.Mittel.Status != MittelStatus.Incomplet));
}
