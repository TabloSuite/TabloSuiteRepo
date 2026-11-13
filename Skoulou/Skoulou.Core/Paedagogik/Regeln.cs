using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Paedagogik;

/// <summary>Abgeschlossener Versuch, wie die Versuchsregel ihn sieht.</summary>
public sealed record VersuchsWert(int Nr, decimal Punkte, decimal MaxPunkte, bool KorrekturOffen = false)
{
    public decimal Anteil => MaxPunkte <= 0 ? 0 : Punkte / MaxPunkte;
}

/// <summary>Versuchsregel einer Zuweisung (Bauplan §11.18): « illimité » oder MaxN, gewertet wird Meilleur/Premier/Dernier.</summary>
public static class VersuchsRegel
{
    public const int MaxVersucheObergrenze = 20;

    /// <summary>Darf ein weiterer Versuch begonnen werden?</summary>
    public static bool DarfNeuenVersuch(int? maxVersuche, int bisherigeVersuche)
        => maxVersuche is null || bisherigeVersuche < maxVersuche.Value;

    /// <summary>Verbleibende Versuche (null = unbegrenzt).</summary>
    public static int? Verbleibend(int? maxVersuche, int bisherigeVersuche)
        => maxVersuche is null ? null : Math.Max(0, maxVersuche.Value - bisherigeVersuche);

    /// <summary>Der gewertete unter den abgeschlossenen Versuchen (null = noch keiner).</summary>
    public static VersuchsWert? Gewertet(IEnumerable<VersuchsWert> abgeschlossene, VersuchsWertung wertung)
    {
        var liste = abgeschlossene.OrderBy(v => v.Nr).ToList();
        if (liste.Count == 0) return null;
        return wertung switch
        {
            VersuchsWertung.Premier => liste[0],
            VersuchsWertung.Dernier => liste[^1],
            // Bester Anteil; bei Gleichstand der frühere Versuch.
            _ => liste.OrderByDescending(v => v.Anteil).ThenBy(v => v.Nr).First()
        };
    }

    public static string Bezeichnung(int? maxVersuche, VersuchsWertung wertung)
    {
        var versuche = maxVersuche switch
        {
            null => "Essais illimités",
            1 => "1 seul essai",
            { } n => $"{n} essais au plus"
        };
        var zaehlt = wertung switch
        {
            VersuchsWertung.Premier => "le premier essai compte",
            VersuchsWertung.Dernier => "le dernier essai compte",
            _ => "le meilleur essai compte"
        };
        return maxVersuche == 1 ? versuche : $"{versuche}, {zaehlt}";
    }
}

/// <summary>Wann ein Schüler Lösungen sieht (Bauplan §4.4; Examen immer nach der Frist).</summary>
public static class LoesungsRegel
{
    /// <param name="frageBeantwortet">Die Frage ist im aktuellen Versuch endgültig beantwortet.</param>
    /// <param name="versuchAbgeschlossen">Der Versuch ist abgegeben.</param>
    public static bool Sichtbar(LoesungsSichtbarkeit regel, DateTime jetztUtc, DateTime? fristUtc,
        bool frageBeantwortet, bool versuchAbgeschlossen)
        => regel switch
        {
            LoesungsSichtbarkeit.Jamais => false,
            LoesungsSichtbarkeit.Immediat => frageBeantwortet || versuchAbgeschlossen,
            // Ohne Frist: nach der eigenen Abgabe.
            _ => fristUtc is { } frist ? jetztUtc >= frist : versuchAbgeschlossen
        };

    public static string Bezeichnung(LoesungsSichtbarkeit regel) => regel switch
    {
        LoesungsSichtbarkeit.Immediat => "Corrigé dès la réponse",
        LoesungsSichtbarkeit.ApresFrist => "Corrigé après la date limite",
        _ => "Corrigé jamais affiché"
    };
}

/// <summary>Zeitregeln für Examen: Fenster, Dauer, 30 s Karenz, Auto-Abgabe.</summary>
public static class PruefungsZeit
{
    public static readonly TimeSpan Karenz = TimeSpan.FromSeconds(30);
    public const int MinDauer = 1;
    public const int MaxDauer = 600;

    /// <summary>Spätestes Abgabeende eines Versuchs: Start + Dauer, aber nie nach der Frist.</summary>
    public static DateTime Deadline(DateTime startUtc, int dauerMinuten, DateTime? fristUtc)
    {
        var ende = startUtc.AddMinutes(dauerMinuten);
        return fristUtc is { } frist && frist < ende ? frist : ende;
    }

    /// <summary>Darf ein Examen jetzt begonnen werden?</summary>
    public static bool Startbar(DateTime jetztUtc, DateTime abUtc, DateTime? fristUtc)
        => jetztUtc >= abUtc && (fristUtc is null || jetztUtc < fristUtc);

    /// <summary>Werden Antworten noch angenommen (bis Deadline + Karenz)?</summary>
    public static bool AnnahmeOffen(DateTime jetztUtc, DateTime? deadlineUtc)
        => deadlineUtc is null || jetztUtc <= deadlineUtc.Value + Karenz;

    /// <summary>Muss der Hintergrunddienst den Versuch abgeben?</summary>
    public static bool AutoAbgabeFaellig(DateTime jetztUtc, DateTime? deadlineUtc)
        => deadlineUtc is { } d && jetztUtc > d + Karenz;

    public static TimeSpan Restzeit(DateTime jetztUtc, DateTime deadlineUtc)
        => deadlineUtc > jetztUtc ? deadlineUtc - jetztUtc : TimeSpan.Zero;

    /// <summary>« 1 h 05 min », « 45 min ».</summary>
    public static string DauerText(int minuten)
        => minuten >= 60 ? $"{minuten / 60} h {minuten % 60:00} min" : $"{minuten} min";
}

/// <summary>Die eine Score-Formel: punktgewichtet (Σ Punkte / Σ Höchstpunkte).</summary>
public static class ScoreRechner
{
    public static decimal? Prozent(decimal punkte, decimal max)
        => max <= 0 ? null : Math.Round(punkte * 100m / max, 2, MidpointRounding.AwayFromZero);

    public static decimal? Gesamt(IEnumerable<(decimal Punkte, decimal Max)> eintraege)
    {
        decimal summe = 0, maxSumme = 0;
        foreach (var (p, m) in eintraege)
        {
            if (m <= 0) continue;
            summe += Math.Clamp(p, 0, m);
            maxSumme += m;
        }
        return Prozent(summe, maxSumme);
    }

    /// <summary>Farbstufe wie in der Alt-App: &lt; 50 rot, = 50 hellrot, 51–75 hellgrün, &gt; 75 grün.</summary>
    public static string Stufe(decimal? prozent) => prozent switch
    {
        null => "neutral",
        < 50 => "rot",
        <= 50 => "hellrot",
        <= 75 => "hellgruen",
        _ => "gruen"
    };

    /// <summary>« 13,5 / 20 » mit höchstens zwei Stellen.</summary>
    public static string Punkte(decimal wert) => wert.ToString("0.##", Werkzeuge.SkoulouKultur.Kultur);
}
