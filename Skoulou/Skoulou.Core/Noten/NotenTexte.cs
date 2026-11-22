using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.Core.Noten;

/// <summary>
/// Texte und Schwellen wie in der Alt-App (Fachkonzept 04 §3.10), aber auf dem gerundeten Wert (Bauplan §5 Regel 4/6):
/// Appréciation, Vorschlag der Distinktion, Sanktionen, Entscheidungen, Ränge und Notenformat.
/// </summary>
public static class NotenTexte
{
    /// <summary>
    /// Kopf des Moyenne-Kastens im Bulletin, grammatisch passend zum (frei änderbaren) Periodentitel:
    /// « MOYENNE DU 1ER TRIMESTRE », « MOYENNE DE LA SESSION 1 », « MOYENNE DE L'EXAMEN BLANC », sonst « MOYENNE : … ».
    /// </summary>
    public static string MoyenneTitel(string periode)
    {
        var t = periode.Trim();
        var klein = t.ToLowerInvariant();
        var gross = t.ToUpper(SkoulouKultur.Kultur);
        if (klein.Contains("trimestre") || klein.Contains("semestre")) return $"MOYENNE DU {gross}";
        if (klein.StartsWith("session") || klein.StartsWith("période") || klein.StartsWith("periode")) return $"MOYENNE DE LA {gross}";
        if (klein.Length > 0 && "aeiouyéèêàâîôûh".Contains(klein[0])) return $"MOYENNE DE L'{gross}";
        return $"MOYENNE : {gross}";
    }

    /// <summary>« 12,50 » (gerundet, fr-FR).</summary>
    public static string Zahl(decimal wert) => NotenRechner.Runde(wert).ToString("0.00", SkoulouKultur.Kultur);

    /// <summary>Punkte einer Note ohne unnötige Nullen: « 12 », « 12,5 », « 12,25 ».</summary>
    public static string Punkte(decimal punkte) => punkte.ToString("0.##", SkoulouKultur.Kultur);

    /// <summary>Anzeige eines Mittels: Wert oder « Non évalué » / « Dispensé » / « Notes incomplètes »; nicht gewähltes Wahlfach « — ».</summary>
    public static string Mittel(Mittel m) => m.Status switch
    {
        MittelStatus.Wert when m.Wert is { } w => Zahl(w),
        MittelStatus.Dispense => "Dispensé",
        MittelStatus.Incomplet => "Notes incomplètes",
        MittelStatus.NonChoisi => "—",
        _ => "Non évalué"
    };

    /// <summary>Regel einer « Matière facultative » für Auswahllisten (Klassen › Matières et enseignants).</summary>
    public static string WahlfachText(WahlfachRegel regel) => regel switch
    {
        WahlfachRegel.WieNormal => "Compte comme une matière normale",
        WahlfachRegel.NurInformation => "Ne compte pas (information seulement)",
        _ => "Seuls les points au-dessus de 10 comptent"
    };

    /// <summary>Kurze Angabe der Regel auf dem Bulletin (Kopfzeile « MATIÈRES FACULTATIVES » bzw. unter dem Fach).</summary>
    public static string WahlfachKurz(WahlfachRegel regel) => regel switch
    {
        WahlfachRegel.WieNormal => "comptent comme les autres matières",
        WahlfachRegel.NurInformation => "à titre d'information, ne comptent pas",
        _ => "points au-dessus de 10 ajoutés"
    };

    /// <summary>Periode, in der der Schüler noch nicht (oder nicht mehr) eingeschrieben war – zählt nicht fürs Jahresmittel.</summary>
    public const string NichtEingeschrieben = "non inscrit(e)";

    /// <summary>Eintrittsperiode eines Neuzugangs ohne eine einzige Note – zählt nicht fürs Jahresmittel (Nutzer 22.11.2026).</summary>
    public const string EintrittOhneNote = "arrivé(e) en cours de période, sans note";

    /// <summary>Kennzeichnung einer Periode im Bilan, die für den Schüler nicht zählt (Bauplan §5 Regel 3).</summary>
    public static string Teilnahme(PeriodenTeilnahme t) => t == PeriodenTeilnahme.EintrittOhneNote ? EintrittOhneNote : NichtEingeschrieben;

    /// <summary>Kurzform für Tabellen: Wert oder « — » / « disp. » / « incomplet ».</summary>
    public static string MittelKurz(Mittel m) => m.Status switch
    {
        MittelStatus.Wert when m.Wert is { } w => Zahl(w),
        MittelStatus.Dispense => "disp.",
        MittelStatus.Incomplet => "incomplet",
        _ => "—"
    };

    /// <summary>« 1er », « 2e », « 3e ex », « Non classé ».</summary>
    public static string Rang(RangInfo rang)
        => rang.Rang is not { } r ? "Non classé" : (r == 1 ? "1er" : $"{r}e") + (rang.Ex ? " ex" : string.Empty);

    /// <summary>Automatische Appréciation (gerundeter Wert).</summary>
    public static string Appreciation(decimal wert) => NotenRechner.Runde(wert) switch
    {
        >= 18 => "Excellent",
        >= 16 => "Très bien",
        >= 14 => "Bien",
        >= 12 => "Assez bien",
        >= 10 => "Passable",
        >= 8 => "Insuffisant",
        >= 5 => "Faible",
        _ => "Très faible"
    };

    /// <summary>Vorschlag aus der Periodenmoyenne (gerundet): ≥ 16 Félicitations, ≥ 14 Encouragements, ≥ 12 Tableau d'honneur.</summary>
    public static Distinktion DistinktionVorschlag(decimal? moyenne) => moyenne is not { } m ? Distinktion.Keine : NotenRechner.Runde(m) switch
    {
        >= 16 => Distinktion.Felicitations,
        >= 14 => Distinktion.Encouragements,
        >= 12 => Distinktion.TableauHonneur,
        _ => Distinktion.Keine
    };

    public static string DistinktionText(Distinktion d) => d switch
    {
        Distinktion.TableauHonneur => "Tableau d'honneur",
        Distinktion.Encouragements => "Tableau d'honneur + Encouragements",
        Distinktion.Felicitations => "Tableau d'honneur + Félicitations",
        _ => "Aucune"
    };

    /// <summary>Einzelne Sanktionen in fester Reihenfolge.</summary>
    public static IReadOnlyList<string> SanktionenTexte(Sanktionen s)
    {
        var liste = new List<string>();
        if (s.HasFlag(Sanktionen.AvertissementTravail)) liste.Add("Avertissement travail");
        if (s.HasFlag(Sanktionen.AvertissementConduite)) liste.Add("Avertissement conduite");
        if (s.HasFlag(Sanktionen.BlameTravail)) liste.Add("Blâme travail");
        if (s.HasFlag(Sanktionen.BlameConduite)) liste.Add("Blâme conduite");
        return liste;
    }

    public static readonly IReadOnlyList<Sanktionen> AlleSanktionen =
        [Sanktionen.AvertissementTravail, Sanktionen.AvertissementConduite, Sanktionen.BlameTravail, Sanktionen.BlameConduite];

    public static string Entscheidung(JahresEntscheidungsArt e) => e switch
    {
        JahresEntscheidungsArt.Admis => "Admis(e) en classe supérieure",
        JahresEntscheidungsArt.Redouble => "Redouble la classe",
        JahresEntscheidungsArt.Exclu => "Exclu(e)",
        JahresEntscheidungsArt.Orientation => "Orienté(e)",
        JahresEntscheidungsArt.Partie => "Quitte l'établissement",
        _ => "À décider"
    };

    public static string EntscheidungKurz(JahresEntscheidungsArt e) => e switch
    {
        JahresEntscheidungsArt.Admis => "Admis",
        JahresEntscheidungsArt.Redouble => "Redouble",
        JahresEntscheidungsArt.Exclu => "Exclu",
        JahresEntscheidungsArt.Orientation => "Orientation",
        JahresEntscheidungsArt.Partie => "Départ",
        _ => "À décider"
    };

    public static string Art(BewertungsArt a) => a switch
    {
        BewertungsArt.Interrogation => "Interrogation",
        BewertungsArt.Devoir => "Devoir",
        BewertungsArt.Composition => "Composition",
        BewertungsArt.Oral => "Oral",
        BewertungsArt.TP => "Travaux pratiques",
        _ => "Autre"
    };

    /// <summary>Kürzel eines Status im Notenraster (« abs », « aj », « disp »).</summary>
    public static string StatusKuerzel(NotenStatus s) => s switch
    {
        NotenStatus.Absent => "abs",
        NotenStatus.AbsentJustifie => "aj",
        NotenStatus.Dispense => "disp",
        _ => string.Empty
    };

    public static string StatusText(NotenStatus s) => s switch
    {
        NotenStatus.Absent => "Absent(e)",
        NotenStatus.AbsentJustifie => "Absence justifiée",
        NotenStatus.Dispense => "Dispensé(e)",
        NotenStatus.Leer => "Note retirée",
        _ => "Noté"
    };

    /// <summary>Anzeige einer Note: Punkte oder Status-Kürzel; ohne Note leer.</summary>
    public static string Note(NotenStatus? status, decimal? punkte) => status switch
    {
        NotenStatus.Notee when punkte is { } p => Punkte(p),
        NotenStatus.Absent or NotenStatus.AbsentJustifie or NotenStatus.Dispense => StatusKuerzel(status.Value),
        _ => string.Empty
    };

    /// <summary>Titelzeile des Bulletins nach Periodenstruktur.</summary>
    public static string BulletinTitel(PeriodenArt art, bool zaehltFuerJahr) => !zaehltFuerJahr
        ? "BULLETIN D'EXAMEN BLANC"
        : art switch
        {
            PeriodenArt.Trimestre => "BULLETIN TRIMESTRIEL DE NOTES",
            PeriodenArt.Semestre => "BULLETIN SEMESTRIEL DE NOTES",
            _ => "BULLETIN DE NOTES"
        };
}
