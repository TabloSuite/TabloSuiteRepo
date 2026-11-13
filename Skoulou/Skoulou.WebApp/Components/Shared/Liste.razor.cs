using Microsoft.AspNetCore.Components;
using Skoulou.Core.Fehler;

namespace Skoulou.WebApp.Components.Shared;

/// <summary>Anfrage an die Ladefunktion einer Liste (Muster SucheAsync(…, suche, ueberspringen, anzahl)).</summary>
public sealed record ListenAnfrage(string? Suche, string? Filter, int Ueberspringen, int Anzahl);

/// <summary>Filter-Chip; Wert null = « Tous ».</summary>
public sealed record FilterChip(string? Wert, string Label, int? Anzahl = null);

/// <summary>
/// Server-geblätterte Liste: lädt seitenweise über <see cref="Laden"/> (Einträge, gibt es mehr?),
/// sucht verzögert (350 ms) und filtert über Chips. <see cref="NeuLadenAsync"/> nach Änderungen aufrufen.
/// </summary>
public partial class Liste<T> : IDisposable
{
    public const int StandardSeite = 50;

    [Parameter, EditorRequired] public Func<ListenAnfrage, CancellationToken, Task<(IReadOnlyList<T> Eintraege, bool Mehr)>> Laden { get; set; } = default!;
    [Parameter, EditorRequired] public RenderFragment<T> Zeile { get; set; } = default!;
    [Parameter] public RenderFragment? TabellenKopf { get; set; }
    [Parameter] public RenderFragment? Werkzeuge { get; set; }
    [Parameter] public RenderFragment? LeerAktion { get; set; }
    [Parameter] public IReadOnlyList<FilterChip> Filter { get; set; } = [];
    [Parameter] public string? AnfangsFilter { get; set; }
    [Parameter] public bool MitSuche { get; set; } = true;
    [Parameter] public string SuchPlatzhalter { get; set; } = "Rechercher…";
    [Parameter] public int Seitengroesse { get; set; } = StandardSeite;
    [Parameter] public bool Scroll { get; set; } = true;
    [Parameter] public string ScrollHoehe { get; set; } = "34rem";
    [Parameter] public string LeerIcon { get; set; } = "inbox";
    [Parameter] public string LeerTitel { get; set; } = "Rien pour le moment";
    [Parameter] public string? LeerText { get; set; }
    [Parameter] public string? CssClass { get; set; }

    private List<T>? eintraege;
    private bool mehr;
    private string? suche;
    private string? filter;
    private string? fehler;
    private CancellationTokenSource? abbruch;

    private bool HatSuche => !string.IsNullOrWhiteSpace(suche) || filter is not null;

    protected override async Task OnInitializedAsync()
    {
        filter = AnfangsFilter;
        await NeuLadenAsync();
    }

    /// <summary>Lädt die erste Seite neu (z. B. nach Speichern oder Löschen).</summary>
    public async Task NeuLadenAsync()
    {
        abbruch?.Cancel();
        abbruch = new CancellationTokenSource();
        await LadeSeiteAsync(neu: true, abbruch.Token);
    }

    private async Task LadeSeiteAsync(bool neu, CancellationToken ct)
    {
        try
        {
            var (seite, weitere) = await Laden(new ListenAnfrage(suche?.Trim(), filter, neu ? 0 : eintraege?.Count ?? 0, Seitengroesse), ct);
            if (ct.IsCancellationRequested) return;
            eintraege = neu ? seite.ToList() : [.. eintraege ?? [], .. seite];
            mehr = weitere;
            fehler = null;
        }
        catch (OperationCanceledException)
        {
            // überholt durch eine neuere Suche
        }
        catch (Exception ex) when (ex is FachFehler or KeinZugriffFehler)
        {
            fehler = ex.Message;
        }
    }

    private async Task SucheGeaendert(ChangeEventArgs e)
    {
        suche = e.Value?.ToString();
        abbruch?.Cancel();
        abbruch = new CancellationTokenSource();
        var token = abbruch.Token;
        try
        {
            await Task.Delay(350, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        await LadeSeiteAsync(neu: true, token);
    }

    private async Task FilterWaehlenAsync(string? wert)
    {
        filter = wert;
        await NeuLadenAsync();
    }

    private Task MehrLadenAsync() => LadeSeiteAsync(neu: false, abbruch?.Token ?? CancellationToken.None);

    public void Dispose()
    {
        abbruch?.Cancel();
        abbruch?.Dispose();
    }
}
