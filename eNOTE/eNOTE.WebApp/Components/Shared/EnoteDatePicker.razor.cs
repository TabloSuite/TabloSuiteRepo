using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ENote.WebApp.Components.Shared;

/// <summary>Moderner Kalender-DatePicker (@bind-Value, nullable DateTime). Schließt bei Klick außerhalb.</summary>
public partial class EnoteDatePicker : IAsyncDisposable
{
    [Parameter] public DateTime? Value { get; set; }
    [Parameter] public EventCallback<DateTime?> ValueChanged { get; set; }
    [Parameter] public string Placeholder { get; set; } = "jj.mm.aaaa";
    [Parameter] public bool Disabled { get; set; }

    [Inject] private IJSRuntime Js { get; set; } = default!;

    private bool offen;
    private DateTime angezeigterMonat = DateTime.Today;
    private DotNetObjectReference<EnoteDatePicker>? selfRef;
    private bool closerRegistriert;

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            angezeigterMonat = new DateTime(Value.Value.Year, Value.Value.Month, 1);
        }
    }

    private async Task SetzeOffenAsync(bool wert)
    {
        offen = wert;
        if (offen && Value is not null)
        {
            // Beim Öffnen immer auf den gesetzten Monat springen.
            angezeigterMonat = new DateTime(Value.Value.Year, Value.Value.Month, 1);
        }

        if (offen && !closerRegistriert)
        {
            selfRef ??= DotNetObjectReference.Create(this);
            try
            {
                await Js.InvokeVoidAsync("enoteRegisterElementCloser", selfRef, "datepicker");
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
            StateHasChanged();
        }
    }

    private void WechsleMonat(int delta)
        => angezeigterMonat = angezeigterMonat.AddMonths(delta);

    private IEnumerable<DateTime?> KalenderZellen()
    {
        var erster = new DateTime(angezeigterMonat.Year, angezeigterMonat.Month, 1);
        // Montag = Wochenstart (französisch).
        var offset = ((int)erster.DayOfWeek + 6) % 7;
        for (var i = 0; i < offset; i++)
        {
            yield return null;
        }

        var tageImMonat = DateTime.DaysInMonth(erster.Year, erster.Month);
        for (var tag = 1; tag <= tageImMonat; tag++)
        {
            yield return new DateTime(erster.Year, erster.Month, tag);
        }
    }

    private async Task WaehleAsync(DateTime tag)
    {
        Value = tag;
        offen = false;
        await ValueChanged.InvokeAsync(tag);
    }

    private async Task HeuteAsync() => await WaehleAsync(DateTime.Today);

    private async Task LoeschenAsync()
    {
        Value = null;
        offen = false;
        await ValueChanged.InvokeAsync(null);
    }

    public async ValueTask DisposeAsync()
    {
        // Abmelden aus der globalen Klick-Liste (enote.js), sonst ruft sie die entsorgte Referenz weiter auf (18.11.2026).
        if (selfRef is not null && closerRegistriert)
        {
            try { await Js.InvokeVoidAsync("enoteUnregisterElementCloser", selfRef); }
            catch (JSDisconnectedException) { }
            catch (InvalidOperationException) { }
            catch (TaskCanceledException) { }
        }
        selfRef?.Dispose();
    }
}
