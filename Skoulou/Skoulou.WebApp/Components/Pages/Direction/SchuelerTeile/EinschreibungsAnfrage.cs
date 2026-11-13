namespace Skoulou.WebApp.Components.Pages.Direction.SchuelerTeile;

/// <summary>Was mit der Einschreibung geschieht.</summary>
public enum EinschreibungsAktion
{
    Einschreiben,
    Wechsel,
    Austritt,
    Stornieren,
    Sammel
}

/// <summary>Anfrage an den Einschreibungsdialog (ein Schüler oder – nur Sammel – mehrere).</summary>
public sealed record EinschreibungsAnfrage(EinschreibungsAktion Art, IReadOnlyList<Guid> SchuelerIds, IReadOnlyList<string> Namen,
    Guid? EinschreibungId = null, string? AktuelleKlasse = null, DateOnly? AktivSeit = null);
