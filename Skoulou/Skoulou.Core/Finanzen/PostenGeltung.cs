namespace Skoulou.Core.Finanzen;

/// <summary>
/// Einschreibung eines Schülers im Jahr, für die Geltung der Gebührenposten. <see cref="Von"/>/<see cref="Bis"/> sind
/// bereits effektiv (Session-Klassen enden mit ihrer Session, <c>SessionRegel</c>).
/// </summary>
public sealed record BelegungDaten(Guid KlasseId, Guid? StufeId, DateOnly Von, DateOnly? Bis, bool Aktiv);

/// <summary>Geltung eines Postens: Stufe/Klasse (beide null = alle) und optional eine Periode/Session.</summary>
public sealed record GeltungDaten(Guid? StufeId, Guid? KlasseId, DateOnly? PeriodeBeginn = null, DateOnly? PeriodeEnde = null);

/// <summary>
/// Welche Posten schuldet ein Schüler? Jahresposten: nach der maßgeblichen Einschreibung (aktiv bzw. zuletzt begonnen).
/// Posten einer Session: nur wer in dieser Session eingeschrieben ist (irgendeine passende Einschreibung schneidet die
/// Session) – keine anteilige Gebühr bei Austritt, wer in der Session war, schuldet sie ganz.
/// </summary>
public static class PostenGeltung
{
    public static BelegungDaten? Massgeblich(IEnumerable<BelegungDaten> einschreibungen)
        => einschreibungen.OrderByDescending(e => e.Aktiv).ThenByDescending(e => e.Von).FirstOrDefault();

    public static bool Gilt(GeltungDaten posten, IReadOnlyCollection<BelegungDaten> einschreibungen)
    {
        if (posten.PeriodeBeginn is not { } beginn || posten.PeriodeEnde is not { } ende)
            return Massgeblich(einschreibungen) is { } m && Passt(posten, m);
        return einschreibungen.Any(e => Passt(posten, e) && e.Von <= ende && (e.Bis is null || (e.Bis >= beginn && e.Bis >= e.Von)));
    }

    private static bool Passt(GeltungDaten p, BelegungDaten e)
        => (p.KlasseId is null && p.StufeId is null) || p.KlasseId == e.KlasseId || (p.KlasseId is null && p.StufeId == e.StufeId);
}
