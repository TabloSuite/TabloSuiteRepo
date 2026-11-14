using Skoulou.Core.Organisation;

namespace Skoulou.Core.Finanzen;

/// <summary>
/// Plattformanteil je Abrechnungsperiode (Bauplan §5 Regel 7): Jahrespreis P auf n Perioden in ganzen FCFA,
/// Anteil(i) = ⌊P·i/n⌋ − ⌊P·(i−1)/n⌋ – die Summe ergibt immer genau P (z. B. 4.166 + 4.167 + 4.167).
/// </summary>
public static class PlattformAnteil
{
    public static long Anteil(long jahrespreis, int nr, int anzahl)
    {
        if (jahrespreis < 0) throw new ArgumentOutOfRangeException(nameof(jahrespreis));
        if (anzahl < 1) throw new ArgumentOutOfRangeException(nameof(anzahl));
        if (nr < 1 || nr > anzahl) throw new ArgumentOutOfRangeException(nameof(nr));
        return jahrespreis * nr / anzahl - jahrespreis * (nr - 1) / anzahl;
    }

    public static IReadOnlyList<long> Anteile(long jahrespreis, int anzahl)
        => Enumerable.Range(1, anzahl).Select(i => Anteil(jahrespreis, i, anzahl)).ToList();
}

/// <summary>
/// Zeitraum einer (nicht stornierten) Einschreibung; bei einer Session-Klasse mit Beginn/Ende der Session
/// (die Einschreibung endet automatisch mit der Session, <see cref="SessionRegel"/>).
/// </summary>
public sealed record EinschreibungsZeitraum(Guid SchuelerId, Guid EinschreibungId, DateOnly Von, DateOnly? Bis,
    DateOnly? SessionBeginn = null, DateOnly? SessionEnde = null)
{
    public bool Schneidet(DateOnly beginn, DateOnly ende) => SessionRegel.Schneidet(Von, Bis, SessionBeginn, SessionEnde, beginn, ende);
}

/// <summary>Abrechnungspflichtiger Schüler einer Periode mit der Einschreibung, die die Position trägt.</summary>
public sealed record AbrechnungsKandidat(Guid SchuelerId, Guid EinschreibungId);

/// <summary>
/// Wer ist in einer Periode abrechnungspflichtig? Jeder Schüler mit einer Einschreibung, deren Zeitraum die Periode
/// schneidet – genau einmal je Periode, auch bei Klassenwechsel (die zuletzt begonnene Einschreibung trägt die Position).
/// Session-Klassen zählen nur in ihrer Session (effektiver Zeitraum).
/// </summary>
public static class Abrechnungspflicht
{
    public static bool Schneidet(DateOnly von, DateOnly? bis, DateOnly beginn, DateOnly ende) => von <= ende && (bis is null || bis >= beginn);

    public static IReadOnlyList<AbrechnungsKandidat> Kandidaten(IEnumerable<EinschreibungsZeitraum> einschreibungen,
        DateOnly beginn, DateOnly ende, IReadOnlySet<Guid>? bereitsAbgerechnet = null)
        => einschreibungen
            .Where(e => e.Schneidet(beginn, ende))
            .Where(e => bereitsAbgerechnet is null || !bereitsAbgerechnet.Contains(e.SchuelerId))
            .GroupBy(e => e.SchuelerId)
            .Select(g => g.OrderByDescending(e => e.Von).ThenByDescending(e => e.EinschreibungId).First())
            .OrderBy(e => e.SchuelerId)
            .Select(e => new AbrechnungsKandidat(e.SchuelerId, e.EinschreibungId))
            .ToList();
}

/// <summary>Anteil des Responsable an einer bestätigten Rechnung: ganze FCFA, abgerundet.</summary>
public static class ResponsableVerguetung
{
    public static long Betrag(long rechnungsBetrag, decimal prozent)
    {
        if (rechnungsBetrag <= 0 || prozent <= 0) return 0;
        return (long)decimal.Floor(rechnungsBetrag * prozent / 100m);
    }
}
