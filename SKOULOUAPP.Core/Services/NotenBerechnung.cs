namespace SkoulouApp.Core.Services;

/// <summary>Fehlende Noten sind kein Nullpunktwert; unvollständige Ergebnisse bleiben offen.</summary>
public static class NotenBerechnung
{
    public static decimal? FachMittel(IEnumerable<NotenAnteil> anteile)
    {
        var liste = anteile.ToList();
        if (liste.Count == 0 || liste.Any(x => x.Punkte is null)) return null;
        if (liste.Any(x => x.Maximum <= 0 || x.Gewicht <= 0 || x.Punkte < 0 || x.Punkte > x.Maximum))
            throw new ArgumentOutOfRangeException(nameof(anteile));
        return liste.Sum(x => x.Punkte!.Value / x.Maximum * 20m * x.Gewicht) / liste.Sum(x => x.Gewicht);
    }

    public static decimal? GesamtMittel(IEnumerable<(decimal? Note, decimal Koeffizient)> faecher)
    {
        var liste = faecher.ToList();
        if (liste.Count == 0 || liste.Any(x => x.Note is null)) return null;
        if (liste.Any(x => x.Koeffizient <= 0 || x.Note < 0 || x.Note > 20)) throw new ArgumentOutOfRangeException(nameof(faecher));
        return liste.Sum(x => x.Note!.Value * x.Koeffizient) / liste.Sum(x => x.Koeffizient);
    }

    public static decimal? JahresMittel(IEnumerable<PeriodenAnteil> perioden)
    {
        // Tests werden selbst bei versehentlich gesetztem Gewicht niemals berücksichtigt.
        var liste = perioden.Where(x => !x.IstTest && x.Gewicht > 0).ToList();
        if (liste.Count == 0 || liste.Any(x => x.Note is null)) return null;
        return GesamtMittel(liste.Select(x => (x.Note, x.Gewicht)));
    }

    public static decimal Anzeige(decimal note) => decimal.Round(note, 2, MidpointRounding.AwayFromZero);
}

public sealed record NotenAnteil(decimal? Punkte, decimal Maximum, decimal Gewicht);
public sealed record PeriodenAnteil(decimal? Note, decimal Gewicht, bool IstTest);
