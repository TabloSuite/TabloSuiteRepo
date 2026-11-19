using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

/// <summary>Moderne Toggle-Checkbox (bool, @bind-Value).</summary>
public partial class EnoteCheckbox
{
    [Parameter] public bool Value { get; set; }
    [Parameter] public EventCallback<bool> ValueChanged { get; set; }
    [Parameter] public string? Label { get; set; }
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Pflicht-Bestätigung: Stern an der Beschriftung (Nutzer 19.11.2026: « Pflichtfelder immer mit Stern »).</summary>
    [Parameter] public bool Pflicht { get; set; }

    private async Task ToggleAsync()
    {
        if (Disabled)
        {
            return;
        }

        Value = !Value;
        await ValueChanged.InvokeAsync(Value);
    }
}
