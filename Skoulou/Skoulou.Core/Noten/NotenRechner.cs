using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Noten;

/// <summary>Art eines Mittelwerts: Wert vorhanden, nicht bewertet, nur Dispens/entschuldigt oder unvollständig.</summary>
public enum MittelStatus
{
    Wert = 0,

    /// <summary>Keine Bewertung (Fach erscheint als « Non évalué », zählt nicht).</summary>
    NonEvalue = 1,

    /// <summary>Bewertungen vorhanden, aber keine zählt (dispensé, absent justifié) – zählt nicht.</summary>
    Dispense = 2,

    /// <summary>Mindestens eine erwartete Note fehlt (« Notes incomplètes »).</summary>
    Incomplet = 3
}

/// <summary>Ungerundeter Mittelwert mit Status; Anzeige, Rang und Schwellen nutzen <see cref="Gerundet"/>.</summary>
public readonly record struct Mittel(MittelStatus Status, decimal? Wert)
{
    public static readonly Mittel NonEvalue = new(MittelStatus.NonEvalue, null);
    public static readonly Mittel Dispense = new(MittelStatus.Dispense, null);
    public static readonly Mittel Incomplet = new(MittelStatus.Incomplet, null);

    public static Mittel Von(decimal wert) => new(MittelStatus.Wert, wert);

    public bool HatWert => Status == MittelStatus.Wert && Wert.HasValue;

    /// <summary>Ein Wert für Anzeige, Rang und Schwellen (Bauplan §5 Regel 4).</summary>
    public decimal? Gerundet => Wert is { } w ? NotenRechner.Runde(w) : null;
}

/// <summary>
/// Eine Bewertung aus Sicht eines Schülers: Barème, Gewicht und seine Note (Status null = keine Note).
/// <see cref="Erwartet"/> = false: die Bewertung lag außerhalb seiner Einschreibung (fehlt nicht, wenn leer).
/// </summary>
public readonly record struct Wertung(decimal Bareme, decimal Gewicht, NotenStatus? Status, decimal? Punkte, bool Erwartet = true)
{
    public static Wertung Fehlt(decimal bareme = 20, decimal gewicht = 1) => new(bareme, gewicht, null, null);
    public static Wertung Note(decimal punkte, decimal bareme = 20, decimal gewicht = 1) => new(bareme, gewicht, NotenStatus.Notee, punkte);
    public static Wertung Mit(NotenStatus status, decimal bareme = 20, decimal gewicht = 1) => new(bareme, gewicht, status, null);

    /// <summary>Hat der Schüler hier eine (nicht entfernte) Note?</summary>
    public bool HatNote => Status is { } s && s != NotenStatus.Leer;
}

/// <summary>Zentrale Rechenregeln der Noten (Bauplan §5 Regeln 1–4), rein und ohne Datenbank.</summary>
public static class NotenRechner
{
    /// <summary>Rundung auf 2 Stellen, kaufmännisch (Regel 4).</summary>
    public static decimal Runde(decimal wert) => Math.Round(wert, 2, MidpointRounding.AwayFromZero);

    /// <summary>Note auf 20 umgerechnet (Teilnote).</summary>
    public static decimal AufZwanzig(decimal punkte, decimal bareme)
    {
        if (bareme <= 0) throw new ArgumentOutOfRangeException(nameof(bareme), "Le barème doit être positif.");
        if (punkte < 0 || punkte > bareme) throw new ArgumentOutOfRangeException(nameof(punkte), "La note dépasse le barème.");
        return punkte * 20 / bareme;
    }

    /// <summary>
    /// Fachmittel einer Periode (Regel 1): Σ(Punkte/Barème·20·Gewicht)/ΣGewicht über « Notee » (+ « Absent » als 0, wenn die
    /// Schule es so eingestellt hat); « AbsentJustifie »/« Dispense » zählen nicht; fehlt eine erwartete Note → unvollständig.
    /// </summary>
    public static Mittel Fachmittel(IEnumerable<Wertung> wertungen, bool absentZaehltNull)
    {
        decimal summe = 0, gewichte = 0;
        var relevant = false;
        var fehlt = false;
        foreach (var w in wertungen)
        {
            if (w.Bareme <= 0) throw new ArgumentOutOfRangeException(nameof(wertungen), "Le barème doit être positif.");
            if (w.Gewicht <= 0) throw new ArgumentOutOfRangeException(nameof(wertungen), "Le poids doit être positif.");
            if (!w.HatNote)
            {
                if (w.Erwartet) { relevant = true; fehlt = true; }
                continue;
            }
            relevant = true;
            switch (w.Status)
            {
                case NotenStatus.Notee:
                    if (w.Punkte is not { } p) throw new ArgumentOutOfRangeException(nameof(wertungen), "Une note sans points.");
                    summe += AufZwanzig(p, w.Bareme) * w.Gewicht;
                    gewichte += w.Gewicht;
                    break;
                case NotenStatus.Absent when absentZaehltNull:
                    gewichte += w.Gewicht;
                    break;
            }
        }
        if (!relevant) return Mittel.NonEvalue;
        if (fehlt) return Mittel.Incomplet;
        return gewichte == 0 ? Mittel.Dispense : Mittel.Von(summe / gewichte);
    }

    /// <summary>
    /// Moyenne générale (Regel 2): Σ(Fachmittel·Koeff)/ΣKoeff über Fächer mit Wert; ein unvollständiges Fach → unvollständig;
    /// « Non évalué »/« Dispensé » zählen nicht; ohne Fach mit Wert → nicht bewertet.
    /// </summary>
    public static Mittel MoyenneGenerale(IEnumerable<(Mittel Mittel, decimal Koeffizient)> faecher)
    {
        decimal summe = 0, koeffizienten = 0;
        var unvollstaendig = false;
        foreach (var (mittel, koeff) in faecher)
        {
            if (koeff <= 0) throw new ArgumentOutOfRangeException(nameof(faecher), "Le coefficient doit être positif.");
            if (mittel.Status == MittelStatus.Incomplet) { unvollstaendig = true; continue; }
            if (!mittel.HatWert) continue;
            summe += mittel.Wert!.Value * koeff;
            koeffizienten += koeff;
        }
        if (unvollstaendig) return Mittel.Incomplet;
        return koeffizienten == 0 ? Mittel.NonEvalue : Mittel.Von(summe / koeffizienten);
    }

    /// <summary>
    /// Jahresmittel (Regel 3): Σ(Periodenmittel·Jahresgewicht)/ΣJahresgewicht über Perioden mit « ZaehltFuerJahr »;
    /// fehlt eine (kein Wert) → unvollständig. Examens blancs werden ignoriert.
    /// </summary>
    public static Mittel Jahresmittel(IEnumerable<(Mittel Mittel, decimal Gewicht, bool ZaehltFuerJahr)> perioden)
    {
        decimal summe = 0, gewichte = 0;
        var irgendeine = false;
        foreach (var (mittel, gewicht, zaehlt) in perioden)
        {
            if (!zaehlt || gewicht <= 0) continue;
            irgendeine = true;
            if (!mittel.HatWert) return Mittel.Incomplet;
            summe += mittel.Wert!.Value * gewicht;
            gewichte += gewicht;
        }
        return irgendeine ? Mittel.Von(summe / gewichte) : Mittel.NonEvalue;
    }

    /// <summary>
    /// Jahresmittel eines Fachs (Bilan): wie <see cref="Jahresmittel"/>, aber Perioden, in denen das Fach nicht bewertet
    /// wurde (« Non évalué »/« Dispensé »), werden übersprungen; unvollständig bleibt unvollständig.
    /// </summary>
    public static Mittel FachJahresmittel(IEnumerable<(Mittel Mittel, decimal Gewicht, bool ZaehltFuerJahr)> perioden)
    {
        decimal summe = 0, gewichte = 0;
        foreach (var (mittel, gewicht, zaehlt) in perioden)
        {
            if (!zaehlt || gewicht <= 0) continue;
            if (mittel.Status == MittelStatus.Incomplet) return Mittel.Incomplet;
            if (!mittel.HatWert) continue;
            summe += mittel.Wert!.Value * gewicht;
            gewichte += gewicht;
        }
        return gewichte == 0 ? Mittel.NonEvalue : Mittel.Von(summe / gewichte);
    }

    /// <summary>Ist die Periode die letzte zählende Periode des Jahres (spätestes Ende)? Dann gibt es den « Bilan de fin d'année ».</summary>
    public static bool IstLetztePeriode(IEnumerable<(Guid Id, DateOnly Ende, bool ZaehltFuerJahr)> perioden, Guid periodeId)
    {
        var letzte = perioden.Where(p => p.ZaehltFuerJahr).OrderByDescending(p => p.Ende).Select(p => (Guid?)p.Id).FirstOrDefault();
        return letzte == periodeId;
    }
}
