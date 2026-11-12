using System.Text.RegularExpressions;

namespace ENote.Core.Services.Kunden;

/// <summary>Regeln für Anwesenheit (« Client présent pour … ») und Kundennotizen.</summary>
public static partial class KundenBesuchRegeln
{
    public const int GrundMax = 120;
    public const int RaumMax = 40;
    public const int NotizMax = 1000;
    /// <summary>Höchstzahl festgelegter Räume eines Ladens (Paramètres › Salles et postes).</summary>
    public const int RaeumeMax = 30;
    /// <summary>So viele Motive (aus dem Katalog) erscheinen als Schnellauswahl.</summary>
    public const int MotiveMax = 8;

    /// <summary>Offene Besuche, die älter sind, gelten als vergessen (Hinweis in der Übersicht).</summary>
    public static readonly TimeSpan VergessenNach = TimeSpan.FromHours(12);

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Leerzeichen();

    /// <summary>Trimmt und fasst Leerzeichen zusammen; Zeilenumbrüche (Notizen) bleiben, max. eine Leerzeile.</summary>
    public static string Bereinige(string? text)
    {
        var zeilen = (text ?? "").Replace("\r\n", "\n").Split('\n').Select(z => Leerzeichen().Replace(z, " ").Trim()).ToList();
        var ergebnis = new List<string>();
        foreach (var z in zeilen)
        {
            if (z.Length == 0 && (ergebnis.Count == 0 || ergebnis[^1].Length == 0)) continue;
            ergebnis.Add(z);
        }
        while (ergebnis.Count > 0 && ergebnis[^1].Length == 0) ergebnis.RemoveAt(ergebnis.Count - 1);
        return string.Join("\n", ergebnis);
    }

    public static (string Grund, string? Raum) PruefeBesuch(string? grund, string? raum)
    {
        var g = Bereinige(grund).Replace('\n', ' ');
        if (g.Length < 2) throw new ArgumentException("Indiquez pourquoi le client est là (ex. coupe, consultation).");
        if (g.Length > GrundMax) throw new ArgumentException($"Motif : {GrundMax} caractères au maximum.");
        var r = Bereinige(raum).Replace('\n', ' ');
        if (r.Length > RaumMax) throw new ArgumentException($"Salle : {RaumMax} caractères au maximum.");
        return (g, r.Length == 0 ? null : r);
    }

    /// <summary>Name eines festgelegten Raums: eine Zeile, 1 bis <see cref="RaumMax"/> Zeichen.</summary>
    public static string PruefeRaumName(string? name)
    {
        var n = Bereinige(name).Replace('\n', ' ');
        if (n.Length == 0) throw new ArgumentException("Indiquez le nom de la salle ou du poste.");
        if (n.Length > RaumMax) throw new ArgumentException($"Salle : {RaumMax} caractères au maximum.");
        return n;
    }

    public static string PruefeNotiz(string? text)
    {
        var t = Bereinige(text);
        if (t.Length == 0) throw new ArgumentException("La note est vide.");
        if (t.Length > NotizMax) throw new ArgumentException($"Note : {NotizMax} caractères au maximum.");
        return t;
    }

    private static int Minuten(DateTime vonUtc, DateTime bisUtc) => Math.Max(0, (int)Math.Floor((bisUtc - vonUtc).TotalMinutes));

    /// <summary>Dauer: „&lt; 1 min“, „35 min“, „1 h 05“.</summary>
    public static string Dauer(DateTime vonUtc, DateTime bisUtc)
    {
        var minuten = Minuten(vonUtc, bisUtc);
        if (minuten < 1) return "< 1 min";
        if (minuten < 60) return $"{minuten} min";
        return $"{minuten / 60} h {minuten % 60:00}";
    }

    /// <summary>Kurz für Abzeichen: „à l'instant“ oder die Dauer.</summary>
    public static string Kurz(DateTime vonUtc, DateTime jetztUtc) => Minuten(vonUtc, jetztUtc) < 1 ? "à l'instant" : Dauer(vonUtc, jetztUtc);

    /// <summary>Im Satz: „à l'instant“ oder „depuis 35 min“.</summary>
    public static string Seit(DateTime vonUtc, DateTime jetztUtc) => Minuten(vonUtc, jetztUtc) < 1 ? "à l'instant" : $"depuis {Dauer(vonUtc, jetztUtc)}";
}
