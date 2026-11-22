using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.WebApp.Components.Pages.Noten;

/// <summary>Zustand einer Zelle im Raster (lokal, nach jedem Speichern aktualisiert).</summary>
internal sealed class ZellZustand(RasterZelle z)
{
    public string Text { get; set; } = z.Text;
    public NotenStatus? Status { get; set; } = z.Status;
    /// <summary>Zuletzt vom Server bekannter Text (Prüfung « inzwischen geändert »).</summary>
    public string Bekannt { get; set; } = z.Text;
    public bool Erwartet { get; } = z.Erwartet;
    public string? Fehler { get; set; }
    public bool Gespeichert { get; set; }
    public bool Laeuft { get; set; }

    public string Klasse => string.Join(' ', new[]
    {
        Fehler is not null ? "fehler" : null,
        Gespeichert ? "ok" : null,
        Laeuft ? "laeuft" : null,
        Status is NotenStatus.Absent or NotenStatus.AbsentJustifie or NotenStatus.Dispense ? "status" : null,
        Erwartet ? null : "fremd"
    }.Where(k => k is not null));
}

/// <summary>Notenraster: speichert jede Zelle sofort, aktualisiert nur die betroffene Zeile (Bauplan §7).</summary>
public partial class NotenRasterTabelle : IAsyncDisposable
{
    [Parameter, EditorRequired] public NotenRaster Raster { get; set; } = default!;
    [Parameter] public bool SpaltenBearbeitbar { get; set; }
    [Parameter] public EventCallback<RasterSpalte> OnSpalte { get; set; }

    /// <summary>Wird nach jeder gespeicherten Note gemeldet (Fortschritt im Kopf).</summary>
    [Parameter] public EventCallback<int> OnFortschritt { get; set; }

    [Inject] private NotenService Noten { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private readonly Dictionary<(Guid, Guid), ZellZustand> zellen = [];
    private readonly Dictionary<Guid, Mittel> mittel = [];
    private readonly Dictionary<Guid, string?> bemerkungen = [];
    private NotenRaster? geladen;
    private ElementReference tabelle;
    private IJSObjectReference? modul;
    private (int Zeile, int Spalte)? aktiv;
    private RasterZeile? bemerkungFuer;
    private IReadOnlyList<NotenHistorie>? historie;
    private string historieTitel = string.Empty;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(geladen, Raster)) return;
        geladen = Raster;
        zellen.Clear();
        mittel.Clear();
        bemerkungen.Clear();
        foreach (var z in Raster.Zeilen)
        {
            mittel[z.EinschreibungId] = z.Mittel;
            bemerkungen[z.EinschreibungId] = z.Bemerkung;
            foreach (var c in z.Zellen) zellen[(c.BewertungId, z.EinschreibungId)] = new ZellZustand(c);
        }
        aktiv = null;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            modul ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/noten.js");
            await modul.InvokeVoidAsync("registriere", tabelle);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
        }
    }

    private ZellZustand Zelle(Guid bewertung, Guid einschreibung) => zellen[(bewertung, einschreibung)];

    private Mittel MittelVon(RasterZeile z) => mittel.GetValueOrDefault(z.EinschreibungId, z.Mittel);

    private string? Bemerkung(RasterZeile z) => bemerkungen.GetValueOrDefault(z.EinschreibungId);

    /// <summary>Anzahl erfasster erwarteter Zellen (für den Fortschritt).</summary>
    public int Erfasst => zellen.Values.Count(z => z.Erwartet && z.Status is { } s && s != NotenStatus.Leer);

    private async Task SpeichernAsync(RasterZeile zeile, RasterSpalte spalte, string? text)
    {
        var zelle = Zelle(spalte.BewertungId, zeile.EinschreibungId);
        zelle.Text = text ?? string.Empty;
        zelle.Fehler = null;
        zelle.Gespeichert = false;
        zelle.Laeuft = true;
        try
        {
            var stand = await Noten.NoteSpeichernAsync(Ich.KontoId, spalte.BewertungId, zeile.EinschreibungId, text, bekannt: zelle.Bekannt);
            zelle.Status = stand.Zelle.Status;
            zelle.Text = stand.Zelle.Text;
            zelle.Bekannt = stand.Zelle.Text;
            if (stand.Konflikt is { } konflikt)
            {
                zelle.Fehler = konflikt;
                Toast.ZeigeFehler(konflikt);
            }
            zelle.Gespeichert = stand.Geaendert;
            mittel[zeile.EinschreibungId] = stand.Mittel;
            await OnFortschritt.InvokeAsync(Erfasst);
        }
        catch (Exception ex) when (ex is FachFehler or KeinZugriffFehler)
        {
            zelle.Fehler = ex.Message;
            Toast.ZeigeFehler(ex.Message);
        }
        finally
        {
            zelle.Laeuft = false;
        }
    }

    private async Task StatusAsync(string kuerzel)
    {
        if (aktiv is not { } a) return;
        await SpeichernAsync(Raster.Zeilen[a.Zeile], Raster.Spalten[a.Spalte], kuerzel);
        if (modul is not null)
        {
            try { await modul.InvokeVoidAsync("fokus", tabelle, a.Zeile, a.Spalte); }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException) { }
        }
    }

    private async Task HistorieAsync()
    {
        if (aktiv is not { } a) return;
        var zeile = Raster.Zeilen[a.Zeile];
        var spalte = Raster.Spalten[a.Spalte];
        historieTitel = $"{zeile.Name} · {spalte.Titel}";
        await AusfuehrenAsync(async () => historie = await Noten.HistorieAsync(Ich.KontoId, spalte.BewertungId, zeile.EinschreibungId));
    }

    private void BemerkungGespeichert(string? text)
    {
        if (bemerkungFuer is { } z) bemerkungen[z.EinschreibungId] = text;
        bemerkungFuer = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (modul is null) return;
        try { await modul.DisposeAsync(); }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException) { }
        GC.SuppressFinalize(this);
    }
}
