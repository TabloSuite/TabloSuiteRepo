using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Components.Shared;

/// <summary>Kalenderfeld für <see cref="DateOnly"/> (@bind-Wert) mit Grenzen Min/Max.</summary>
public partial class DatumFeld : IAsyncDisposable
{
    [Parameter] public string? Label { get; set; }
    [Parameter] public DateOnly? Wert { get; set; }
    [Parameter] public EventCallback<DateOnly?> WertChanged { get; set; }
    [Parameter] public DateOnly? Min { get; set; }
    [Parameter] public DateOnly? Max { get; set; }
    [Parameter] public string Platzhalter { get; set; } = "jj/mm/aaaa";
    [Parameter] public string? Hinweis { get; set; }
    [Parameter] public string? Fehler { get; set; }
    [Parameter] public bool Pflicht { get; set; }
    [Parameter] public bool Deaktiviert { get; set; }
    [Parameter] public string? CssClass { get; set; }

    /// <summary>« Heute » in der Schulzeitzone (Standard: lokales Datum des Servers in Lomé).</summary>
    [Parameter] public DateOnly? HeuteWert { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private TimeProvider Zeit { get; set; } = default!;

    private static readonly string[] Wochentage = ["Lu", "Ma", "Me", "Je", "Ve", "Sa", "Di"];
    private readonly string id = Feld.NeueId("datum");
    private bool offen;
    private DateOnly monat;
    private DotNetObjectReference<DatumFeld>? selbst;

    private DateOnly Heute => HeuteWert ?? new SchulZeit(Zeit).Heute;
    private string MonatsTitel => monat.ToString("MMMM yyyy", SkoulouKultur.Kultur);

    private bool Erlaubt(DateOnly tag) => (Min is null || tag >= Min) && (Max is null || tag <= Max);

    private async Task UmschaltenAsync()
    {
        if (Deaktiviert) return;
        offen = !offen;
        if (!offen) return;
        var basis = Wert ?? Heute;
        monat = new DateOnly(basis.Year, basis.Month, 1);
        selbst ??= DotNetObjectReference.Create(this);
        try { await JS.InvokeVoidAsync("skCloserRegistrieren", selbst, id); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException) { }
    }

    [JSInvokable]
    public void SchliesseVonAussen()
    {
        if (!offen) return;
        offen = false;
        StateHasChanged();
    }

    private void Monat(int delta) => monat = monat.AddMonths(delta);

    private IEnumerable<DateOnly?> Zellen()
    {
        var versatz = ((int)monat.DayOfWeek + 6) % 7; // Montag = erste Spalte
        for (var i = 0; i < versatz; i++) yield return null;
        for (var t = 1; t <= DateTime.DaysInMonth(monat.Year, monat.Month); t++) yield return new DateOnly(monat.Year, monat.Month, t);
    }

    private async Task WaehleAsync(DateOnly? tag)
    {
        Wert = tag;
        offen = false;
        await WertChanged.InvokeAsync(tag);
    }

    public async ValueTask DisposeAsync()
    {
        if (selbst is null) return;
        await JsAufraeumen.VersuchenAsync(JS, "skCloserEntfernen", id);
        selbst.Dispose();
    }
}
