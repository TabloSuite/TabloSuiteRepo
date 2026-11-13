namespace Skoulou.WebApp.Components.Pages.Direction.Gemeinsam;

/// <summary>Ausdrückliche Kontoaktionen der Direktion (der Status ändert sich nie implizit).</summary>
public enum KontoAktion
{
    Passwort,
    Sperren,
    Entsperren,
    Archivieren,
    Reintegrieren
}

/// <summary>Aktion für ein oder (nur Passwort) mehrere Konten.</summary>
public sealed record KontoAktionAnfrage(KontoAktion Art, IReadOnlyList<Guid> KontoIds, IReadOnlyList<string> Namen)
{
    public static KontoAktionAnfrage Eins(KontoAktion art, Guid id, string name) => new(art, [id], [name]);
}
