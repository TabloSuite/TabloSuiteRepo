using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

/// <summary>Passwort-Feld mit Auge-Button zum Ein-/Ausblenden.</summary>
public partial class PasswordInput
{
    [Parameter] public string? Value { get; set; }
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    [Parameter] public string? Name { get; set; }
    [Parameter] public bool Required { get; set; }
    [Parameter] public string? Autocomplete { get; set; }
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Zusätzliche HTML-Attribute (id, inputmode, maxlength, pattern, data-*, aria-*).</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // Über @bind:get/@bind:set (nicht value + @oninput): Blazor gleicht dann mit dem Stand im Browser ab und schreibt
    // bei langsamer Verbindung keine veralteten Zeichen in das Feld zurück (Klick-Simulation S2).
    private async Task SetzeAsync(string? wert)
    {
        Value = wert;
        await ValueChanged.InvokeAsync(Value);
    }
}
