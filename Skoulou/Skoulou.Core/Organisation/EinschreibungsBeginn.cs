namespace Skoulou.Core.Organisation;

/// <summary>
/// Beginn einer Einschreibung (Klicktest 20.11.2026): Eine Schule, die mitten im Jahr mit SKOULOU startet, erfasst ihre
/// Schüler « depuis la rentrée ». Standard ist deshalb der Beginn des Schuljahres – sonst stünden alle Schüler im
/// Notenraster als « Arrivé(e) le … » und frühere Bewertungen wären grau. Ein echter Neuzugang mitten im Jahr ändert
/// das Datum im Dialog. Endete eine frühere Einschreibung desselben Jahres, beginnt die neue frühestens am Tag danach.
/// </summary>
public static class EinschreibungsBeginn
{
    /// <summary>Vorgabe = Beginn des Schuljahres bzw. der Tag nach dem Ende der letzten Einschreibung desselben Jahres.</summary>
    public static DateOnly Vorgabe(DateOnly jahrBeginn, DateOnly jahrEnde, IEnumerable<DateOnly?>? fruehereEnden = null)
    {
        var tag = jahrBeginn;
        foreach (var ende in fruehereEnden ?? [])
            if (ende is { } e && e >= tag) tag = e.AddDays(1);
        return tag > jahrEnde ? jahrEnde : tag;
    }

    /// <summary>Spätestes Datum im Feld « Inscrit(e) depuis le »: heute im laufenden Jahr, sonst das Jahresende.</summary>
    public static DateOnly Hoechstens(DateOnly jahrBeginn, DateOnly jahrEnde, DateOnly heute)
        => heute < jahrBeginn || heute > jahrEnde ? jahrEnde : heute;
}
