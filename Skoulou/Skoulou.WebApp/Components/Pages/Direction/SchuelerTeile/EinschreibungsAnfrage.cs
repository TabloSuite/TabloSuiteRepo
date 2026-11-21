namespace Skoulou.WebApp.Components.Pages.Direction.SchuelerTeile;

/// <summary>Was mit der Einschreibung geschieht.</summary>
public enum EinschreibungsAktion
{
    Einschreiben,
    Wechsel,
    Austritt,
    Stornieren,
    Sammel,

    /// <summary>« Corriger la date d'inscription » (Beginn einer bestehenden Einschreibung).</summary>
    Beginn
}

/// <summary>
/// Anfrage an den Einschreibungsdialog (ein Schüler oder – nur Sammel – mehrere). Bei « Beginn » grenzen
/// <paramref name="Min"/>/<paramref name="Max"/> das Datum auf das Schuljahr der Einschreibung ein.
/// </summary>
public sealed record EinschreibungsAnfrage(EinschreibungsAktion Art, IReadOnlyList<Guid> SchuelerIds, IReadOnlyList<string> Namen,
    Guid? EinschreibungId = null, string? AktuelleKlasse = null, DateOnly? AktivSeit = null, DateOnly? Min = null, DateOnly? Max = null);
