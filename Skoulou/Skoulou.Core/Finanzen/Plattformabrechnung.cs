using Skoulou.Core.Organisation;

namespace Skoulou.Core.Finanzen;

/// <summary>
/// Plattformanteil je Abrechnungsperiode (Bauplan §5 Regel 7, Nutzerentscheidung 19.11.2026): Preis je Schüler und Monat ×
/// Monate der Periode, in ganzen FCFA. Monate = max(1, round(Tage / 30,4375)), Tage = Ende − Beginn + 1 (beide eingeschlossen);
/// z. B. 01.10.–20.12. (81 Tage) = 3, 01.10.–15.02. (138 Tage) = 5. Ferien zwischen den Perioden kosten damit nichts.
/// </summary>
public static class PlattformAnteil
{
    /// <summary>Mittlere Monatslänge (365,25 / 12).</summary>
    public const decimal TageJeMonat = 30.4375m;

    /// <summary>Monate einer « année scolaire complète » für die Orientierung « environ 10 × Monatspreis » (z. B. 5.000 bei 500).</summary>
    public const int MonateJeSchuljahr = 10;

    /// <summary>Monate eines Zeitraums [beginn, ende]: kaufmännisch gerundet, mindestens 1.</summary>
    public static int Monate(DateOnly beginn, DateOnly ende)
    {
        if (ende < beginn) throw new ArgumentOutOfRangeException(nameof(ende));
        var tage = ende.DayNumber - beginn.DayNumber + 1;
        return Math.Max(1, (int)Math.Round(tage / TageJeMonat, MidpointRounding.AwayFromZero));
    }

    /// <summary>Anteil je Schüler für eine Periode = Monatspreis × Monate der Periode.</summary>
    public static long Anteil(long monatspreis, DateOnly beginn, DateOnly ende)
    {
        if (monatspreis < 0) throw new ArgumentOutOfRangeException(nameof(monatspreis));
        return monatspreis * Monate(beginn, ende);
    }
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
