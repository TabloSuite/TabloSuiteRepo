using Skoulou.Core.Entitaeten;
using Skoulou.Core.Paedagogik;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Paedagogik;

namespace Skoulou.WebApp.Components.Pages.Paedagogik;

/// <summary>Französische Anzeigetexte und Zeitformate des Bereichs (Zeiten in der Zeitzone der Schule).</summary>
public static class PaedagogikText
{
    public static string Zeitpunkt(SchulZeit zeit, DateTime utc)
    {
        var lokal = zeit.ZuLokal(utc);
        return $"{SkoulouKultur.Datum(DateOnly.FromDateTime(lokal))} à {SkoulouKultur.Uhrzeit(TimeOnly.FromDateTime(lokal))}";
    }

    /// <summary>« dans 3 jours », « aujourd'hui à 18:00 », « dépassée ».</summary>
    public static string Frist(SchulZeit zeit, DateTime? fristUtc)
    {
        if (fristUtc is not { } f) return "Sans date limite";
        var jetzt = zeit.JetztUtc;
        if (f <= jetzt) return $"Date limite passée ({Zeitpunkt(zeit, f)})";
        var lokal = zeit.ZuLokal(f);
        var tage = DateOnly.FromDateTime(lokal).DayNumber - zeit.Heute.DayNumber;
        var uhr = SkoulouKultur.Uhrzeit(TimeOnly.FromDateTime(lokal));
        return tage switch
        {
            0 => $"À rendre aujourd'hui à {uhr}",
            1 => $"À rendre demain à {uhr}",
            < 7 => $"À rendre dans {tage} jours",
            _ => $"À rendre le {SkoulouKultur.Datum(DateOnly.FromDateTime(lokal))}"
        };
    }

    public static string Art(InhaltsArt art) => art == InhaltsArt.Lecon ? "Leçon" : "Examen";

    public static string Status(InhaltsStatus status) => status switch
    {
        InhaltsStatus.Prete => "Prête",
        InhaltsStatus.Archive => "Archivée",
        _ => "Brouillon"
    };

    public static string StatusArt(InhaltsStatus status) => status switch
    {
        InhaltsStatus.Prete => "erfolg",
        InhaltsStatus.Archive => "neutral",
        _ => "warn"
    };

    public static string Prozent(decimal? prozent) => prozent is { } p ? $"{p.ToString("0.#", SkoulouKultur.Kultur)} %" : "–";

    public static string Punkte(decimal punkte, decimal max) => $"{ScoreRechner.Punkte(punkte)} / {ScoreRechner.Punkte(max)}";

    /// <summary>CSS-Klasse der Score-Farbe (rot, hellrot, hellgruen, gruen, neutral).</summary>
    public static string ScoreKlasse(decimal? prozent) => "sk-score-" + ScoreRechner.Stufe(prozent);

    public static string Ergebnis(BewertungsStatus status) => status switch
    {
        BewertungsStatus.Richtig => "Bonne réponse",
        BewertungsStatus.Teilweise => "Réponse en partie juste",
        BewertungsStatus.Falsch => "Réponse incorrecte",
        BewertungsStatus.Manuell => "En attente de correction",
        _ => "Sans réponse"
    };

    public static string ErgebnisArt(BewertungsStatus status) => status switch
    {
        BewertungsStatus.Richtig => "erfolg",
        BewertungsStatus.Teilweise => "warn",
        BewertungsStatus.Falsch => "fehler",
        BewertungsStatus.Manuell => "info",
        _ => "neutral"
    };

    public static string Fortschritt(SchuelerFortschritt f) => f switch
    {
        SchuelerFortschritt.Termine => "Terminé",
        SchuelerFortschritt.EnCours => "En cours",
        SchuelerFortschritt.ACorriger => "À corriger",
        _ => "Pas commencé"
    };

    public static string FortschrittArt(SchuelerFortschritt f) => f switch
    {
        SchuelerFortschritt.Termine => "erfolg",
        SchuelerFortschritt.EnCours => "info",
        SchuelerFortschritt.ACorriger => "warn",
        _ => "neutral"
    };

    public static string Zustand(PruefungsZustand z) => z switch
    {
        PruefungsZustand.AVenir => "À venir",
        PruefungsZustand.Ouvert => "Ouvert",
        PruefungsZustand.EnCours => "En cours",
        PruefungsZustand.Envoye => "Copie envoyée",
        PruefungsZustand.Corrige => "Corrigé",
        _ => "Non passé"
    };

    public static string ZustandArt(PruefungsZustand z) => z switch
    {
        PruefungsZustand.Ouvert or PruefungsZustand.EnCours => "info",
        PruefungsZustand.Corrige => "erfolg",
        PruefungsZustand.Manque => "fehler",
        _ => "neutral"
    };
}
