namespace Skoulou.Core.Organisation;

/// <summary>
/// Beginn einer Einschreibung (Klicktest 20.11.2026, verfeinert 21.11.2026): Eine Schule, die mitten im Jahr mit SKOULOU
/// startet, erfasst ihre Schüler « depuis la rentrée » – in ihrer Startphase ist deshalb der Beginn des Schuljahres
/// vorbelegt, sonst stünden alle Schüler im Notenraster als « Arrivé(e) le … » und frühere Bewertungen wären grau.
/// Sonst ist ein neuer Schüler ein echter Neuzugang: Vorgabe heute (<see cref="Standard"/>). Die Direktion kann das Datum
/// im Dialog immer ändern. Endete eine frühere Einschreibung desselben Jahres, beginnt die neue frühestens am Tag danach.
/// </summary>
public static class EinschreibungsBeginn
{
    /// <summary>Vorgabe = <paramref name="jahrBeginn"/> (der <see cref="Standard"/>) bzw. der Tag nach dem Ende der letzten Einschreibung desselben Jahres.</summary>
    public static DateOnly Vorgabe(DateOnly jahrBeginn, DateOnly jahrEnde, IEnumerable<DateOnly?>? fruehereEnden = null)
    {
        var tag = jahrBeginn;
        foreach (var ende in fruehereEnden ?? [])
            if (ende is { } e && e >= tag) tag = e.AddDays(1);
        return tag > jahrEnde ? jahrEnde : tag;
    }

    /// <summary>
    /// Erster Tag der Vorgabe (vor der Regel « nach dem Ende der letzten Einschreibung »), 21.11.2026 nach der Simulation:
    /// Der Schuljahresbeginn ist nur in der <b>Startphase einer Schule</b> vorbelegt, die sich mitten im Jahr bei SKOULOU
    /// angemeldet hat – von der Anmeldung bis zum Ende der Periode, in der sie sich angemeldet hat (dann erfasst sie ihre
    /// Schüler « depuis la rentrée »). Sonst – Schule schon vor dem Jahr bei SKOULOU oder Startphase vorbei – ist ein neuer
    /// Schüler ein echter Neuzugang: Vorgabe <b>heute</b> (im Schuljahr), sonst zählten seine früheren Bewertungen als
    /// fehlend und schon abgerechnete Perioden würden nachberechnet. Die Direktion kann das Datum immer ändern.
    /// </summary>
    public static DateOnly Standard(DateOnly jahrBeginn, DateOnly jahrEnde, DateOnly heute, DateOnly anmeldung,
        IEnumerable<(DateOnly Beginn, DateOnly Ende)> perioden)
    {
        if (anmeldung > jahrBeginn)
        {
            var startphaseBis = perioden.Where(p => p.Ende >= anmeldung).Select(p => (DateOnly?)p.Ende).Min() ?? jahrEnde;
            if (heute <= startphaseBis) return jahrBeginn;
        }
        return heute < jahrBeginn ? jahrBeginn : heute > jahrEnde ? jahrEnde : heute;
    }

    /// <summary>Spätestes Datum im Feld « Inscrit(e) depuis le »: heute im laufenden Jahr, sonst das Jahresende.</summary>
    public static DateOnly Hoechstens(DateOnly jahrBeginn, DateOnly jahrEnde, DateOnly heute)
        => heute < jahrBeginn || heute > jahrEnde ? jahrEnde : heute;
}
