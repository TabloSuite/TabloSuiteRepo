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

    private async Task OnInput(ChangeEventArgs e)
    {
        Value = e.Value?.ToString();
        await ValueChanged.InvokeAsync(Value);
    }
}
