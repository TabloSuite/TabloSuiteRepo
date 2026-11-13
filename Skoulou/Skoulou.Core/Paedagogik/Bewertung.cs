using System.Text.Json;
using Skoulou.Core.Entitaeten;

namespace Skoulou.Core.Paedagogik;

/// <summary>Antwortmöglichkeit einer Auswahlfrage, wie die Engine sie sieht.</summary>
public sealed record OptionDefinition(Guid Id, bool Richtig);

/// <summary>Bewertungsrelevanter Teil einer Frage (ohne Darstellung).</summary>
public sealed record FrageDefinition(
    FragenTyp Typ,
    decimal Punkte,
    IReadOnlyList<OptionDefinition> Optionen,
    IReadOnlyList<string> AkzeptierteAntworten,
    IReadOnlyList<IReadOnlyList<string>> Luecken,
    bool AkzenteBeachten = false);

/// <summary>Antwort eines Schülers (wird als JSON in <c>Antwort.AntwortJson</c> gespeichert).</summary>
public sealed record AntwortEingabe
{
    public static readonly AntwortEingabe Leer = new();

    public IReadOnlyList<Guid> Optionen { get; init; } = [];
    public string? Text { get; init; }
    public IReadOnlyList<string?> Luecken { get; init; } = [];

    public bool IstLeer => Optionen.Count == 0 && string.IsNullOrWhiteSpace(Text) && Luecken.All(string.IsNullOrWhiteSpace);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string AlsJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Liest gespeichertes JSON; Unlesbares gilt als leere Antwort.</summary>
    public static AntwortEingabe AusJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Leer;
        try { return JsonSerializer.Deserialize<AntwortEingabe>(json, Json) ?? Leer; }
        catch (JsonException) { return Leer; }
    }
}

/// <summary>Ergebnis einer Bewertung.</summary>
public enum BewertungsStatus
{
    Richtig,
    Teilweise,
    Falsch,

    /// <summary>Keine Antwort gegeben (0 Punkte).</summary>
    Leer,

    /// <summary>Text ohne Musterlösung: die Lehrkraft korrigiert.</summary>
    Manuell
}

/// <param name="Punkte">null = wartet auf manuelle Korrektur.</param>
public sealed record BewertungsErgebnis(BewertungsStatus Status, decimal? Punkte, decimal MaxPunkte,
    int? RichtigeLuecken = null, int? AnzahlLuecken = null)
{
    public bool BrauchtKorrektur => Status == BewertungsStatus.Manuell;
}

/// <summary>
/// Die eine Bewertungs-Engine für Übung und Examen (Bauplan §5.11): punktbasiert, Teilpunkte bei
/// Mehrfachauswahl (richtige − falsche, nie unter 0) und je Lücke, toleranter Textvergleich,
/// Texte ohne Musterlösung → manuelle Korrektur.
/// </summary>
public static class BewertungsEngine
{
    public static BewertungsErgebnis Bewerte(FrageDefinition frage, AntwortEingabe? antwort)
    {
        antwort ??= AntwortEingabe.Leer;
        var max = Math.Max(0, frage.Punkte);
        return frage.Typ switch
        {
            FragenTyp.ChoixUnique or FragenTyp.AudioChoixUnique => Einfach(frage, antwort, max),
            FragenTyp.ChoixMultiple or FragenTyp.AudioChoixMultiple => Mehrfach(frage, antwort, max),
            FragenTyp.TexteATrous => Luecken(frage, antwort, max),
            _ => Text(frage, antwort, max)
        };
    }

    private static List<Guid> Gueltige(FrageDefinition f, AntwortEingabe a)
        => a.Optionen.Distinct().Where(id => f.Optionen.Any(o => o.Id == id)).ToList();

    private static BewertungsErgebnis Einfach(FrageDefinition f, AntwortEingabe a, decimal max)
    {
        var gewaehlt = Gueltige(f, a);
        if (gewaehlt.Count == 0) return new(BewertungsStatus.Leer, 0, max);
        if (gewaehlt.Count > 1) return new(BewertungsStatus.Falsch, 0, max);
        return f.Optionen.First(o => o.Id == gewaehlt[0]).Richtig
            ? new(BewertungsStatus.Richtig, max, max)
            : new(BewertungsStatus.Falsch, 0, max);
    }

    private static BewertungsErgebnis Mehrfach(FrageDefinition f, AntwortEingabe a, decimal max)
    {
        var gewaehlt = Gueltige(f, a);
        if (gewaehlt.Count == 0) return new(BewertungsStatus.Leer, 0, max);
        var richtige = f.Optionen.Where(o => o.Richtig).Select(o => o.Id).ToHashSet();
        if (richtige.Count == 0) return new(BewertungsStatus.Falsch, 0, max);

        var treffer = gewaehlt.Count(richtige.Contains);
        var falsche = gewaehlt.Count - treffer;
        var netto = Math.Max(0, treffer - falsche);
        var punkte = Runde(max * netto / richtige.Count);
        var status = netto == richtige.Count ? BewertungsStatus.Richtig
            : netto > 0 ? BewertungsStatus.Teilweise : BewertungsStatus.Falsch;
        return new(status, punkte, max);
    }

    private static BewertungsErgebnis Text(FrageDefinition f, AntwortEingabe a, decimal max)
    {
        if (string.IsNullOrWhiteSpace(a.Text)) return new(BewertungsStatus.Leer, 0, max);
        var varianten = f.AkzeptierteAntworten.Where(v => AntwortVergleich.Normalisiere(v, f.AkzenteBeachten).Length > 0).ToList();
        if (varianten.Count == 0) return new(BewertungsStatus.Manuell, null, max);
        return AntwortVergleich.Passt(a.Text, varianten, f.AkzenteBeachten)
            ? new(BewertungsStatus.Richtig, max, max)
            : new(BewertungsStatus.Falsch, 0, max);
    }

    private static BewertungsErgebnis Luecken(FrageDefinition f, AntwortEingabe a, decimal max)
    {
        var anzahl = f.Luecken.Count;
        if (anzahl == 0) return new(BewertungsStatus.Falsch, 0, max, 0, 0);
        if (a.Luecken.All(string.IsNullOrWhiteSpace)) return new(BewertungsStatus.Leer, 0, max, 0, anzahl);

        var treffer = 0;
        for (var i = 0; i < anzahl; i++)
        {
            var eingabe = i < a.Luecken.Count ? a.Luecken[i] : null;
            if (AntwortVergleich.Passt(eingabe, f.Luecken[i], f.AkzenteBeachten)) treffer++;
        }
        var punkte = Runde(max * treffer / anzahl);
        var status = treffer == anzahl ? BewertungsStatus.Richtig
            : treffer > 0 ? BewertungsStatus.Teilweise : BewertungsStatus.Falsch;
        return new(status, punkte, max, treffer, anzahl);
    }

    private static decimal Runde(decimal wert) => Math.Round(wert, 2, MidpointRounding.AwayFromZero);
}
