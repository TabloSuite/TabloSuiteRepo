using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

/// <summary>
/// Knopf für Server-Aktionen: sperrt sich während der Ausführung und zeigt einen Spinner.
/// Inhalt entweder als <see cref="Text"/> oder frei (z. B. Icon + Text) als <see cref="ChildContent"/>.
/// </summary>
public partial class AsyncActionButton
{
    [Parameter] public string Text { get; set; } = "Enregistrer";
    [Parameter] public string? BusyText { get; set; }
    [Parameter] public string Type { get; set; } = "button";
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Grundklasse; leer lassen für Knöpfe mit eigenem Stil (Icon-Knöpfe, Karten-Knöpfe).</summary>
    [Parameter] public string BasisKlasse { get; set; } = "enote-btn";

    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Ladezustand von außen (22.11.2026): für Formulare, die per Enter oder Klick über ihr <c>onsubmit</c> absenden
    /// (<see cref="Type"/> = « submit », ohne <see cref="OnClick"/>). Zeigt den Spinner und sperrt den Knopf wie ein Klick –
    /// ein gesperrter Submit-Knopf verhindert auch ein zweites Absenden per Enter.
    /// </summary>
    [Parameter] public bool Busy { get; set; }

    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Weitere Attribute wie title oder aria-label.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private bool klickLaeuft;

    /// <summary>Läuft gerade eine Aktion – über den eigenen Klick oder von außen (<see cref="Busy"/>)?</summary>
    private bool IsBusy => klickLaeuft || Busy;

    private string KlassenListe => string.Join(' ', new[] { BasisKlasse, CssClass, IsBusy ? "enote-laedt" : null }.Where(k => !string.IsNullOrWhiteSpace(k)));

    /// <summary>Text neben dem Spinner: BusyText, sonst Text – bei freiem Inhalt ohne BusyText nur der Spinner.</summary>
    private string? BusyLabel => !string.IsNullOrWhiteSpace(BusyText) ? BusyText : ChildContent is null ? Text : null;

    private async Task HandleClickAsync()
    {
        if (IsBusy || !OnClick.HasDelegate)
        {
            return;
        }

        klickLaeuft = true;
        try
        {
            await OnClick.InvokeAsync();
        }
        finally
        {
            klickLaeuft = false;
        }
    }
}
