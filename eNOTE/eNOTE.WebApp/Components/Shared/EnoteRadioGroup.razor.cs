using Microsoft.AspNetCore.Components;

namespace ENote.WebApp.Components.Shared;

/// <summary>Eine Option für EnoteRadioGroup.</summary>
public class EnoteRadioOption<TValue>
{
    public required TValue Value { get; set; }
    public required string Label { get; set; }
}

/// <summary>Moderne Pill-Radio-Gruppe (@bind-Value).</summary>
public partial class EnoteRadioGroup<TValue>
{
    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }
    [Parameter] public IReadOnlyList<EnoteRadioOption<TValue>> Options { get; set; } = [];
    [Parameter] public bool Disabled { get; set; }

    private async Task WaehleAsync(TValue wert)
    {
        if (Disabled)
        {
            return;
        }

        Value = wert;
        await ValueChanged.InvokeAsync(wert);
    }
}
