using Skoulou.Core.Werkzeuge;

namespace Skoulou.Core.Organisation;

/// <summary>Eine belegte Stunde (Eintrag eines Stundenplans) mit allem, was die Konfliktprüfung braucht.</summary>
public sealed record Belegung(
    Guid EintragId, Guid PlanId, Guid KlasseId, string Klasse, DateOnly GueltigAb, DateOnly GueltigBis, bool Veroeffentlicht,
    DayOfWeek Tag, TimeOnly Beginn, TimeOnly Ende, Guid LehrerId, string Lehrer, string Fach, string? Raum)
{
    public Zeitspanne Zeit => new(Beginn, Ende);
    public Zeitraum Gueltigkeit => new(GueltigAb, GueltigBis);
}

/// <summary>
/// Konfliktprüfung im Stundenplan: zwei Stunden kollidieren, wenn sie am selben Wochentag zeitlich überlappen
/// (angrenzend ist erlaubt), ihre Pläne gleichzeitig gelten und sie dieselbe Klasse, dieselbe Lehrkraft oder
/// denselben Raum betreffen. Verglichen wird gegen den eigenen Plan und alle veröffentlichten Pläne der Schule.
/// </summary>
public static class StundenplanKonflikte
{
    /// <summary>Raumvergleich ohne Groß-/Kleinschreibung und Leerzeichen-Varianten.</summary>
    public static string? RaumSchluessel(string? raum) => string.IsNullOrWhiteSpace(raum) ? null : TextNormalisierung.Schluessel(raum);

    /// <summary>Erster Konflikt der Stunde mit den anderen Belegungen als Meldung, sonst null.</summary>
    public static string? Pruefe(Belegung neu, IEnumerable<Belegung> andere)
    {
        var raum = RaumSchluessel(neu.Raum);
        foreach (var b in andere)
        {
            if (b.EintragId == neu.EintragId || b.Tag != neu.Tag) continue;
            if (b.PlanId != neu.PlanId && !b.Veroeffentlicht) continue;
            if (!b.Zeit.Ueberschneidet(neu.Zeit) || !b.Gueltigkeit.Ueberschneidet(neu.Gueltigkeit)) continue;

            var wann = $"le {Wochentage.Name(b.Tag).ToLowerInvariant()} de {b.Zeit}";
            if (b.PlanId == neu.PlanId || b.KlasseId == neu.KlasseId)
                return $"Conflit : la classe {b.Klasse} a déjà cours {wann} ({b.Fach}).";
            if (b.LehrerId == neu.LehrerId)
                return $"Conflit : {b.Lehrer} enseigne déjà en {b.Klasse} {wann}.";
            if (raum is not null && RaumSchluessel(b.Raum) == raum)
                return $"Conflit : la salle {b.Raum} est occupée par la classe {b.Klasse} {wann}.";
        }
        return null;
    }
}
