using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Skoulou.WebApp.Components.Shared;

/// <summary>Eine Option für Auswahlfelder und Radiogruppen.</summary>
public sealed record AuswahlOption<T>(T Wert, string Label, string? Zusatz = null, string? Icon = null);

/// <summary>Dropdown mit Suche (@bind-Wert); schließt bei Klick außerhalb (JS-Closer).</summary>
public partial class AuswahlFeld<T> : IAsyncDisposable
{
    [Parameter] public string? Label { get; set; }
    [Parameter] public T? Wert { get; set; }
    [Parameter] public EventCallback<T?> WertChanged { get; set; }
    [Parameter] public IReadOnlyList<AuswahlOption<T>> Optionen { get; set; } = [];
    [Parameter] public string Platzhalter { get; set; } = "Choisir…";
    [Parameter] public bool? MitSuche { get; set; }
    [Parameter] public string? Hinweis { get; set; }
    [Parameter] public string? Fehler { get; set; }
    [Parameter] public bool Pflicht { get; set; }
    [Parameter] public bool Deaktiviert { get; set; }
    [Parameter] public bool Kompakt { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter] public string? CssClass { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly string id = Feld.NeueId("sel");
    private bool offen;
    private string suche = string.Empty;
    private DotNetObjectReference<AuswahlFeld<T>>? selbst;

    private AuswahlOption<T>? gewaehlt => Optionen.FirstOrDefault(o => EqualityComparer<T>.Default.Equals(o.Wert, Wert));
    private bool SucheAktiv => MitSuche ?? Optionen.Count >= 8;

    private IEnumerable<AuswahlOption<T>> Gefiltert => string.IsNullOrWhiteSpace(suche)
        ? Optionen
        : Optionen.Where(o => o.Label.Contains(suche, StringComparison.OrdinalIgnoreCase)
                              || (o.Zusatz?.Contains(suche, StringComparison.OrdinalIgnoreCase) ?? false));

    private async Task UmschaltenAsync()
    {
        if (Deaktiviert) return;
        offen = !offen;
        suche = string.Empty;
        if (!offen) return;
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

    private async Task WaehleAsync(T wert)
    {
        Wert = wert;
        offen = false;
        await WertChanged.InvokeAsync(wert);
    }

    public async ValueTask DisposeAsync()
    {
        if (selbst is null) return;
        await JsAufraeumen.VersuchenAsync(JS, "skCloserEntfernen", id);
        selbst.Dispose();
    }
}
