namespace ENote.WebApp.Services.Formation;

/// <summary>Art eines Abschnitts einer Lektion (Darstellung auf der Seite).</summary>
public enum AbschnittArt
{
    /// <summary>Fließtext mit optionalem Zwischentitel.</summary>
    Text,
    /// <summary>Aufzählung (Punkte) mit optionalem Titel.</summary>
    Liste,
    /// <summary>Nummerierte Schritte « Où cliquer ».</summary>
    Schritte,
    /// <summary>Hervorgehobener Merksatz (« À retenir »).</summary>
    Merke,
    /// <summary>Warnung (« Attention »).</summary>
    Achtung,
    /// <summary>Bildschirmfoto aus der App (Datei unter wwwroot/formation/enote/).</summary>
    Bild,
    /// <summary>Lernvideo aus dem <see cref="VideoKatalog"/> (Datei = Videodatei).</summary>
    Video
}

/// <summary>Ein Abschnitt einer Lektion.</summary>
public sealed record Abschnitt(AbschnittArt Art, string Text, string? Titel = null, string? Datei = null, IReadOnlyList<string>? Punkte = null)
{
    public static Abschnitt T(string text, string? titel = null) => new(AbschnittArt.Text, text, titel);
    public static Abschnitt L(string titel, params string[] punkte) => new(AbschnittArt.Liste, "", titel, Punkte: punkte);
    public static Abschnitt S(string titel, params string[] schritte) => new(AbschnittArt.Schritte, "", titel, Punkte: schritte);
    public static Abschnitt M(string text) => new(AbschnittArt.Merke, text);
    public static Abschnitt A(string text) => new(AbschnittArt.Achtung, text);
    public static Abschnitt B(string datei, string legende) => new(AbschnittArt.Bild, legende, Datei: datei);
    public static Abschnitt V(string videoDatei, string warum) => new(AbschnittArt.Video, warum, Datei: videoDatei);
}

/// <summary>
/// Eine Frage (Übung in der Lektion oder Prüfungsfrage). Mehr als eine richtige Antwort = Mehrfachauswahl
/// (« plusieurs réponses »); gewertet wird nur die vollständig richtige Auswahl.
/// </summary>
public sealed record Frage(string Id, string Text, IReadOnlyList<string> Antworten, IReadOnlyList<int> Richtig, string Erklaerung)
{
    public bool Mehrfach => Richtig.Count > 1;

    public bool IstRichtig(IEnumerable<int>? auswahl)
        => auswahl is not null && auswahl.Distinct().OrderBy(i => i).SequenceEqual(Richtig.OrderBy(i => i));
}

/// <summary>Eine Lektion des Lernpfads: Inhalt, Übungen (nicht bewertet) und Prüfungsfragen (Pool).</summary>
public sealed record Lektion(
    string Id, string Titel, string Kurz, int Minuten, string Icon,
    IReadOnlyList<Abschnitt> Abschnitte, IReadOnlyList<Frage> Uebungen, IReadOnlyList<Frage> Pruefung)
{
    public IEnumerable<string> Videos => Abschnitte.Where(a => a.Art == AbschnittArt.Video && a.Datei is not null).Select(a => a.Datei!);
}
