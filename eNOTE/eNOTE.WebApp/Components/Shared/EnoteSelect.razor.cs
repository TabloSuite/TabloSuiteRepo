using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ENote.WebApp.Components.Shared;

/// <summary>Eine Option für EnoteSelect.</summary>
public class EnoteSelectOption<TValue>
{
    public required TValue Value { get; set; }
    public required string Label { get; set; }
}

/// <summary>Modernes Custom-Dropdown mit optionaler Suche (@bind-Value). Schließt bei Klick außerhalb.</summary>
public partial class EnoteSelect<TValue> : IAsyncDisposable
{
    [Parameter] public TValue? Value { get; set; }
    [Parameter] public EventCallback<TValue?> ValueChanged { get; set; }
    [Parameter] public IReadOnlyList<EnoteSelectOption<TValue>> Options { get; set; } = [];
    [Parameter] public string Placeholder { get; set; } = "Choisir…";
    [Parameter] public bool MitSuche { get; set; }
    [Parameter] public bool Disabled { get; set; }

    [Inject] private IJSRuntime Js { get; set; } = default!;

    private bool offen;
    private string suchText = string.Empty;
    private DotNetObjectReference<EnoteSelect<TValue>>? selfRef;
    private bool closerRegistriert;

    private IEnumerable<EnoteSelectOption<TValue>> GefilterteOptionen =>
        string.IsNullOrWhiteSpace(suchText)
            ? Options
            : Options.Where(o => o.Label.Contains(suchText, StringComparison.OrdinalIgnoreCase));

    private string AktuellerText
    {
        get
        {
            var gewaehlt = Options.FirstOrDefault(o => EqualityComparer<TValue>.Default.Equals(o.Value, Value));
            return gewaehlt is not null ? gewaehlt.Label : Placeholder;
        }
    }

    private async Task ToggleAsync()
    {
        if (Disabled)
        {
            return;
        }

        offen = !offen;
        if (!offen)
        {
            suchText = string.Empty;
        }
        else if (!closerRegistriert)
        {
            selfRef ??= DotNetObjectReference.Create(this);
            try
            {
                await Js.InvokeVoidAsync("enoteRegisterElementCloser", selfRef, "select2");
                closerRegistriert = true;
            }
            catch (InvalidOperationException)
            {
                // JS noch nicht verfügbar.
            }
        }
    }

    [JSInvokable]
    public void SchliesseVonAussen()
    {
        if (offen)
        {
            offen = false;
            suchText = string.Empty;
            StateHasChanged();
        }
    }

    private async Task WaehleAsync(TValue wert)
    {
        Value = wert;
        offen = false;
        suchText = string.Empty;
        await ValueChanged.InvokeAsync(wert);
    }

    public ValueTask DisposeAsync()
    {
        selfRef?.Dispose();
        return ValueTask.CompletedTask;
    }
}
