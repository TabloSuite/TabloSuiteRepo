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
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Weitere Attribute wie title oder aria-label.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private bool IsBusy { get; set; }

    private string KlassenListe => string.Join(' ', new[] { BasisKlasse, CssClass, IsBusy ? "enote-laedt" : null }.Where(k => !string.IsNullOrWhiteSpace(k)));

    /// <summary>Text neben dem Spinner: BusyText, sonst Text – bei freiem Inhalt ohne BusyText nur der Spinner.</summary>
    private string? BusyLabel => !string.IsNullOrWhiteSpace(BusyText) ? BusyText : ChildContent is null ? Text : null;

    private async Task HandleClickAsync()
    {
        if (IsBusy || !OnClick.HasDelegate)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await OnClick.InvokeAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
