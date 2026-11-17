namespace Skoulou.WebApp.Services.Formation;

// Inhalte der Formation (fest im Code, wie in eNOTE). Eigene Namen (Lern…), damit sie nicht mit den Entitäten der
// Pädagogik (Frage, Lecon …) oder der Komponente <Abschnitt> verwechselt werden.

public enum LernAbschnittArt { Text, Liste, Schritte, Merke, Achtung, Bild, Video }

/// <summary>Ein Abschnitt einer Lektion.</summary>
public sealed record LernAbschnitt(LernAbschnittArt Art, string Text, string? Titel = null, string? Datei = null, IReadOnlyList<string>? Punkte = null)
{
    public static LernAbschnitt T(string text, string? titel = null) => new(LernAbschnittArt.Text, text, titel);
    public static LernAbschnitt L(string titel, params string[] punkte) => new(LernAbschnittArt.Liste, "", titel, Punkte: punkte);
    public static LernAbschnitt S(string titel, params string[] schritte) => new(LernAbschnittArt.Schritte, "", titel, Punkte: schritte);
    public static LernAbschnitt M(string text) => new(LernAbschnittArt.Merke, text);
    public static LernAbschnitt A(string text) => new(LernAbschnittArt.Achtung, text);
    /// <summary>Bildschirmfoto (Datei unter wwwroot/formation/skoulou/).</summary>
    public static LernAbschnitt B(string datei, string legende) => new(LernAbschnittArt.Bild, legende, Datei: datei);
    /// <summary>Lernvideo aus dem VideoKatalog (Datei = Videodatei).</summary>
    public static LernAbschnitt V(string videoDatei, string warum) => new(LernAbschnittArt.Video, warum, Datei: videoDatei);
}

/// <summary>Frage (Übung oder Prüfung); mehr als eine richtige Antwort = Mehrfachauswahl, nur vollständig richtig zählt.</summary>
public sealed record LernFrage(string Id, string Text, IReadOnlyList<string> Antworten, IReadOnlyList<int> Richtig, string Erklaerung)
{
    public bool Mehrfach => Richtig.Count > 1;
    public bool IstRichtig(IEnumerable<int>? auswahl)
        => auswahl is not null && auswahl.Distinct().OrderBy(i => i).SequenceEqual(Richtig.OrderBy(i => i));
}

/// <summary>Lektion: Inhalt, Übungen (zählen nicht) und Prüfungsfragen (Pool).</summary>
public sealed record LernLektion(string Id, string Titel, string Kurz, int Minuten, string Icon,
    IReadOnlyList<LernAbschnitt> Abschnitte, IReadOnlyList<LernFrage> Uebungen, IReadOnlyList<LernFrage> Pruefung)
{
    public IEnumerable<string> Videos => Abschnitte.Where(a => a.Art == LernAbschnittArt.Video && a.Datei is not null).Select(a => a.Datei!);
}
