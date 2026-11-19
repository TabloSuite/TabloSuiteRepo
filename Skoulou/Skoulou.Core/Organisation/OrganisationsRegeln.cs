using System.Text;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.Core.Organisation;

/// <summary>Kalenderzeitraum [Beginn, Ende] (beide Tage eingeschlossen).</summary>
public readonly record struct Zeitraum(DateOnly Beginn, DateOnly Ende)
{
    public bool Ueberschneidet(Zeitraum anderer) => Beginn <= anderer.Ende && anderer.Beginn <= Ende;
    public bool Enthaelt(DateOnly tag) => tag >= Beginn && tag <= Ende;
    public bool Enthaelt(Zeitraum z) => z.Beginn >= Beginn && z.Ende <= Ende;
    public int Tage => Ende.DayNumber - Beginn.DayNumber + 1;
    public override string ToString() => $"du {SkoulouKultur.Datum(Beginn)} au {SkoulouKultur.Datum(Ende)}";
}

/// <summary>Uhrzeitspanne einer Stunde; angrenzende Stunden überschneiden sich nicht.</summary>
public readonly record struct Zeitspanne(TimeOnly Beginn, TimeOnly Ende)
{
    public bool Ueberschneidet(Zeitspanne andere) => Beginn < andere.Ende && andere.Beginn < Ende;
    public int Minuten => (int)(Ende - Beginn).TotalMinutes;
    public override string ToString() => $"{SkoulouKultur.Uhrzeit(Beginn)} – {SkoulouKultur.Uhrzeit(Ende)}";
}

/// <summary>Vorschlag einer Periode beim Anlegen der Periodenstruktur.</summary>
public sealed record PeriodenVorschlag(int Nr, string Titel, DateOnly Beginn, DateOnly Ende);

/// <summary>
/// Periodenstruktur eines Schuljahres: Trimestre (3), Semestre (2), Session (frei, Standard 4), Libre (frei).
/// « 1 période » (ein Bulletin am Jahresende) = Libre mit Anzahl 1, Titel <see cref="EinePeriodeTitel"/> (19.11.2026, ohne neuen Enum-Wert).
/// Die Perioden teilen das Jahr lückenlos in gleich lange Abschnitte; Daten sind danach frei änderbar.
/// </summary>
public static class PeriodenPlan
{
    public const int MaxPerioden = 12;

    /// <summary>Titel der einzigen Periode eines Jahres mit « 1 période » (Bulletin « MOYENNE DE L'ANNÉE SCOLAIRE »).</summary>
    public const string EinePeriodeTitel = "Année scolaire";

    /// <summary>Jahr mit genau einer Periode (Libre, Anzahl 1) – im Dialog die Option « 1 période ».</summary>
    public static bool IstEinePeriode(PeriodenArt art, int anzahl) => art == PeriodenArt.Libre && anzahl == 1;

    /// <summary>Bezeichnung mit Anzahl der zählenden Perioden: « 1 période » statt « Périodes libres ».</summary>
    public static string Bezeichnung(PeriodenArt art, int anzahl) => IstEinePeriode(art, anzahl) ? "1 période" : Bezeichnung(art);

    public static int StandardAnzahl(PeriodenArt art) => art switch
    {
        PeriodenArt.Trimestre => 3,
        PeriodenArt.Semestre => 2,
        PeriodenArt.Session => 4,
        _ => 1
    };

    /// <summary>Feste Anzahl (Trimestre, Semestre) oder frei wählbar.</summary>
    public static bool AnzahlFrei(PeriodenArt art) => art is PeriodenArt.Session or PeriodenArt.Libre;

    public static string Bezeichnung(PeriodenArt art) => art switch
    {
        PeriodenArt.Trimestre => "Trimestres",
        PeriodenArt.Semestre => "Semestres",
        PeriodenArt.Session => "Sessions",
        _ => "Périodes libres"
    };

    /// <summary>Titel der n-ten Periode (« 1er trimestre », « 2e semestre », « Session 3 »).</summary>
    public static string Titel(PeriodenArt art, int nr) => art switch
    {
        PeriodenArt.Trimestre => nr == 1 ? "1er trimestre" : $"{nr}e trimestre",
        PeriodenArt.Semestre => nr == 1 ? "1er semestre" : $"{nr}e semestre",
        PeriodenArt.Session => $"Session {nr}",
        _ => $"Période {nr}"
    };

    /// <summary>Teilt [beginn, ende] in gleich lange, lückenlose Perioden.</summary>
    public static IReadOnlyList<PeriodenVorschlag> Erzeuge(PeriodenArt art, int anzahl, DateOnly beginn, DateOnly ende)
    {
        if (ende <= beginn) throw new FachFehler("La date de fin doit être postérieure à la date de début.");
        var n = AnzahlFrei(art) ? anzahl : StandardAnzahl(art);
        if (n < 1 || n > MaxPerioden) throw new FachFehler($"Choisissez entre 1 et {MaxPerioden} périodes.");
        var tage = ende.DayNumber - beginn.DayNumber + 1;
        if (tage < n * 7) throw new FachFehler("L'année est trop courte pour ce nombre de périodes (au moins une semaine chacune).");

        var liste = new List<PeriodenVorschlag>(n);
        for (var i = 0; i < n; i++)
        {
            var von = beginn.AddDays((int)((long)tage * i / n));
            var bis = beginn.AddDays((int)((long)tage * (i + 1) / n) - 1);
            liste.Add(new PeriodenVorschlag(i + 1, IstEinePeriode(art, n) ? EinePeriodeTitel : Titel(art, i + 1), von, bis));
        }
        return liste;
    }

    /// <summary>
    /// Prüft die Perioden eines Jahres: im Jahr, Ende ≥ Beginn, keine Überschneidung unter den Perioden,
    /// die für das Jahr zählen; ein « Examen blanc » (zählt nicht) darf in einer Periode liegen,
    /// aber keinem anderen Examen blanc überschneiden. Meldung oder null.
    /// </summary>
    public static string? Pruefe(Zeitraum jahr, IEnumerable<(string Titel, Zeitraum Zeitraum, bool ZaehltFuerJahr)> perioden)
    {
        var liste = perioden.ToList();
        foreach (var p in liste)
        {
            if (p.Zeitraum.Ende < p.Zeitraum.Beginn) return $"« {p.Titel} » : la date de fin précède la date de début.";
            if (!jahr.Enthaelt(p.Zeitraum)) return $"« {p.Titel} » doit se situer dans l'année scolaire ({jahr}).";
        }
        for (var i = 0; i < liste.Count; i++)
            for (var j = i + 1; j < liste.Count; j++)
            {
                var a = liste[i];
                var b = liste[j];
                if (a.ZaehltFuerJahr == b.ZaehltFuerJahr && a.Zeitraum.Ueberschneidet(b.Zeitraum))
                    return $"« {a.Titel} » et « {b.Titel} » se chevauchent.";
            }
        return null;
    }
}

/// <summary>Französische Wochentage, Montag zuerst.</summary>
public static class Wochentage
{
    public static readonly IReadOnlyList<DayOfWeek> Schulwoche =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    public static readonly IReadOnlyList<DayOfWeek> Alle = [.. Schulwoche, DayOfWeek.Sunday];

    public static string Name(DayOfWeek tag) => tag switch
    {
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        DayOfWeek.Friday => "Vendredi",
        DayOfWeek.Saturday => "Samedi",
        _ => "Dimanche"
    };

    /// <summary>0 = Montag … 6 = Sonntag.</summary>
    public static int Index(DayOfWeek tag) => ((int)tag + 6) % 7;

    /// <summary>Montag der Woche, in der der Tag liegt.</summary>
    public static DateOnly Montag(DateOnly tag) => tag.AddDays(-Index(tag.DayOfWeek));
}

/// <summary>Matrikel: Großbuchstaben, Ziffern und « - / . _ », höchstens 60 Zeichen, eindeutig je Schule.</summary>
public static class MatrikelRegel
{
    public const int MaxLaenge = 60;
    public const string MeldungFormat = "Le matricule ne peut contenir que des lettres, des chiffres et les signes - / . _ (60 caractères au maximum).";

    /// <summary>Getrimmt, groß, ohne Akzente, Leerzeichen entfernt; leer → null.</summary>
    public static string? Normalisiere(string? eingabe)
    {
        if (string.IsNullOrWhiteSpace(eingabe)) return null;
        var text = TextNormalisierung.OhneAkzente(eingabe.Trim()).ToUpperInvariant();
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }

    public static bool IstGueltig(string? matrikel)
        => matrikel is { Length: > 0 and <= MaxLaenge } && matrikel.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '/' or '.' or '_');
}
