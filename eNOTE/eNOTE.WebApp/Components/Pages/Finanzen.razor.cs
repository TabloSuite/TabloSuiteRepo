using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Finanzen(
    FinanzService finanzService,
    BestandsService bestandsService,
    ToastService toast,
    KatalogService katalogService,
    KassenService kassenService,
    AktiverSalonState aktiverSalon,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    LadeZustand ladeZustand) : IAsyncDisposable
{
    private bool isLoading = true;
    private bool darfFinanzenSehen;
    private Guid salonId;
    private TimeZoneInfo Zone => aktiverSalon.Zeitzone;
    /// <summary>Heutiges Datum im Laden (nicht auf dem Server).</summary>
    private DateTime Heute => ENote.Core.Services.Zeitzonen.Heute(Zone, ENote.Core.Services.AppUhr.UtcNow);
    private TimeSpan Versatz => ENote.Core.Services.Zeitzonen.Versatz(Zone, ENote.Core.Services.AppUhr.UtcNow);
    private Guid benutzerId;
    private string salonName = string.Empty;

    private List<FinanzService.TagesUmsatz> tagesUmsaetze = [];
    private List<FinanzService.VerkaufEintrag> letzteVerkaeufe = [];
    private List<Core.DataModels.Kasse.KassenSitzung> abschluesse = [];
    /// <summary>« Par caissier » je Clôture, an der mehrere Personen kassiert haben.</summary>
    private Dictionary<Guid, List<KassenService.KassiererAnteil>> abschlussAnteile = [];
    private FinanzService.FinanzUebersicht? uebersicht;

    private HubConnection? hubVerbindung;
    private decimal liveTagesUmsatz;
    private int liveAnzahlHeute;
    private bool liveVerbunden;

    private DateTime? filterVon = ENote.Core.Services.AppUhr.Today.AddDays(-13);
    private DateTime? filterBis = ENote.Core.Services.AppUhr.Today;
    private bool filterAktiv;

    // Tabs
    private string aktiverTab = "umsatz";
    private void WechsleTab(string tab) => aktiverTab = tab;

    // Suche/Filter für die Verkaufsliste
    private string verkaufsSuche = string.Empty;
    private string zahlungsArtFilter = "alle"; // alle | bar | karte

    private static readonly IReadOnlyList<ENote.WebApp.Components.Shared.EnoteSelectOption<string>> zahlungsArtOptionen =
    [
        new() { Value = "alle", Label = "Tous les paiements" },
        new() { Value = "bar", Label = "Espèces" },
        new() { Value = "karte", Label = "Carte" },
        new() { Value = "mobile", Label = "Mobile Money" }
    ];

    // Suche, Zahlungsart und Zeitraum laufen in der Datenbank; geladen wird seitenweise (« Afficher plus »).
    private IEnumerable<FinanzService.VerkaufEintrag> GefilterteVerkaeufe() => letzteVerkaeufe;
    private bool verkaeufeMehr;
    private CancellationTokenSource? verkaufSuchVerzoegerung;

    private Core.DataModels.Kasse.ZahlungsArt? GewaehlteZahlungsArt => zahlungsArtFilter switch
    {
        "bar" => Core.DataModels.Kasse.ZahlungsArt.Bar,
        "karte" => Core.DataModels.Kasse.ZahlungsArt.Karte,
        "mobile" => Core.DataModels.Kasse.ZahlungsArt.MobileMoney,
        _ => null
    };

    /// <summary>Zeitraum der Verkaufsliste: der gewählte Filter, sonst ohne Grenze (neueste zuerst).</summary>
    private (DateTime? Von, DateTime? Bis) VerkaufsZeitraum()
    {
        if (!filterAktiv) return (null, null);
        var (vonUtc, bisEnde) = ENote.Core.Services.Zeitzonen.TageInUtc(filterVon ?? Heute, filterBis ?? Heute, Zone);
        return (vonUtc, bisEnde);
    }

    private async Task LadeVerkaeufeAsync()
    {
        var (von, bis) = VerkaufsZeitraum();
        (letzteVerkaeufe, verkaeufeMehr) = await finanzService.SucheVerkaeufeAsync(salonId, von, bis, verkaufsSuche, GewaehlteZahlungsArt, 0);
    }

    private async Task MehrVerkaeufeAsync()
    {
        var (von, bis) = VerkaufsZeitraum();
        var (neu, mehr) = await finanzService.SucheVerkaeufeAsync(salonId, von, bis, verkaufsSuche, GewaehlteZahlungsArt, letzteVerkaeufe.Count);
        letzteVerkaeufe.AddRange(neu);
        verkaeufeMehr = mehr;
    }

    private async Task SucheVerkaeufeVerzoegertAsync()
    {
        verkaufSuchVerzoegerung?.Cancel();
        var cts = verkaufSuchVerzoegerung = new CancellationTokenSource();
        try { await Task.Delay(350, cts.Token); }
        catch (TaskCanceledException) { return; }
        await LadeVerkaeufeAsync();
    }

    private string ZeitraumTitel => filterAktiv
        ? $"Chiffre d'affaires du {filterVon:dd'/'MM'/'yyyy} au {filterBis:dd'/'MM'/'yyyy}"
        : "Chiffre d'affaires (14 jours)";

    private string ZeitraumLabel => aktiverZeitraum switch
    {
        "7" => "7 derniers jours",
        "14" => "14 derniers jours",
        "30" => "30 derniers jours",
        "mois" => "ce mois",
        _ => $"du {filterVon:dd'/'MM} au {filterBis:dd'/'MM'/'yyyy}"
    };

    protected override async Task OnInitializedAsync()
    {
        using var _ = ladeZustand.Beginnen();
        aktiverSalon.OnChange += HandleSalonWechsel;

        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        benutzerId = userId;
        if (!aktiverSalon.IstGeladen)
        {
            await aktiverSalon.InitialisiereAsync(userId);
        }

        var aktive = aktiverSalon.Aktiv;
        if (aktive is null)
        {
            isLoading = false;
            return;
        }

        salonId = aktive.SalonId;
        salonName = aktive.Salon.Name;

        // Besitzer oder delegierter Kassierer mit Finanzrecht (serverseitig geprüft).
        var istBesitzer = authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Besitzer);
        darfFinanzenSehen = istBesitzer || aktive.KannFinanzenSehen;

        if (!darfFinanzenSehen)
        {
            isLoading = false;
            return;
        }

        await LadeAsync();
        await StarteLiveVerbindungAsync();
    }

    private void HandleSalonWechsel()
    {
        var aktive = aktiverSalon.Aktiv;
        if (aktive is null || aktive.SalonId == salonId || !darfFinanzenSehen)
        {
            return;
        }

        salonId = aktive.SalonId;
        salonName = aktive.Salon.Name;
        _ = InvokeAsync(async () => { await LadeAsync(); StateHasChanged(); });
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        tagesUmsaetze = await finanzService.GetTagesUmsaetzeAsync(salonId, versatz: Versatz);
        await LadeVerkaeufeAsync();
        abschluesse = await finanzService.GetAbschluesseAsync(salonId);
        abschlussAnteile = await finanzService.GetAbschlussAnteileAsync(abschluesse.Select(a => a.Id).ToList());
        uebersicht = await finanzService.GetFinanzUebersichtAsync(salonId, StandardZeitraumVon(), DateTime.MaxValue);
        bewegungen = await bestandsService.GetBewegungenAsync(salonId, StandardZeitraumVon(), DateTime.MaxValue);

        var heute = tagesUmsaetze.FirstOrDefault(t => t.Datum == Heute);
        liveTagesUmsatz = heute?.Gesamt ?? 0m;
        liveAnzahlHeute = heute?.AnzahlVerkaeufe ?? 0;

        isLoading = false;
    }

    private async Task StarteLiveVerbindungAsync()
    {
        hubVerbindung = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/umsatz"))
            .WithAutomaticReconnect()
            .Build();

        hubVerbindung.On<decimal, int>("UmsatzAktualisiert", (gesamt, anzahl) =>
        {
            liveTagesUmsatz = gesamt;
            liveAnzahlHeute = anzahl;
            InvokeAsync(StateHasChanged);
        });

        hubVerbindung.Reconnected += async _ =>
        {
            liveVerbunden = true;
            await InvokeAsync(StateHasChanged);
        };

        try
        {
            await hubVerbindung.StartAsync();
            await hubVerbindung.InvokeAsync("AbonniereSalon", UmsatzHub.SalonSchluessel(salonId));
            liveVerbunden = true;
        }
        catch
        {
            liveVerbunden = false; // Seite funktioniert auch ohne Live-Update.
        }
    }

    private async Task WendeFilterAsync()
    {
        // Kein seitenweites „Chargement…“: die Zahlen werden an Ort und Stelle ersetzt.
        filterAktiv = true;
        // Kalendertage des Ladens → UTC-Bereich (in Kamerun beginnt der Tag eine Stunde früher in UTC)
        var (vonUtc, bisEnde) = ENote.Core.Services.Zeitzonen.TageInUtc(filterVon ?? Heute, filterBis ?? Heute, Zone);
        tagesUmsaetze = await finanzService.GetTagesUmsaetzeImZeitraumAsync(salonId, vonUtc, bisEnde, versatz: Versatz);
        uebersicht = await finanzService.GetFinanzUebersichtAsync(salonId, vonUtc, bisEnde);
        bewegungen = await bestandsService.GetBewegungenAsync(salonId, vonUtc, bisEnde);
        await LadeVerkaeufeAsync();
    }

    // --- Mouvements: Einlagen/Hilfen, Entnahmen, Warenabgänge ---
    private BestandsService.Bewegungen? bewegungen;
    private bool apportOffen;
    private decimal apportBetrag;
    private Core.DataModels.Finanzen.GeldOrt apportOrt = Core.DataModels.Finanzen.GeldOrt.AusserhalbKasse;
    private string? apportHerkunft;
    private string? apportNotiz;
    private DateTime? apportDatum = ENote.Core.Services.AppUhr.Today;
    private string? apportFehler;

    /// <summary>Pflichtangaben des Apports (wie BestandsService.ErfasseEinlageAsync): Betrag und Provenance.</summary>
    private bool ApportVollstaendig => apportBetrag > 0 && !string.IsNullOrWhiteSpace(apportHerkunft);
    private static readonly string[] HerkunftVorschlaege = ["Aide familiale", "Microcrédit / prêt", "Apport personnel", "Subvention", "Tontine"];

    /// <summary>Name des Kassierers der offenen Kasse (null = keine Kasse offen → « Dans la caisse » gesperrt).</summary>
    private string? offeneKasseVon;

    private async Task OeffneApport()
    {
        var offene = await kassenService.GetOffeneSitzungAsync(salonId);
        offeneKasseVon = offene is null ? null : offene.Kassierer?.FullName ?? "caisse ouverte";
        apportBetrag = 0m;
        apportOrt = Core.DataModels.Finanzen.GeldOrt.AusserhalbKasse;
        apportHerkunft = null;
        apportNotiz = null;
        apportDatum = Heute;
        apportFehler = null;
        apportOffen = true;
    }

    private async Task SpeichereApportAsync()
    {
        var datum = apportDatum is DateTime d ? DateTime.SpecifyKind(d.Date.AddHours(12), DateTimeKind.Utc) : (DateTime?)null;
        var ergebnis = await bestandsService.ErfasseEinlageAsync(benutzerId, salonId, apportBetrag, apportOrt, apportHerkunft, apportNotiz, datum);
        if (!ergebnis.Erfolg)
        {
            apportFehler = ergebnis.Fehler;
            return;
        }

        apportOffen = false;
        toast.ZeigeErfolg($"Apport de {apportBetrag:N0} F enregistré.");
        aktiverTab = "mouvements";
        if (filterAktiv) await WendeFilterAsync(); else await LadeAsync();
    }

    private string aktiverZeitraum = "14";

    private async Task WaehleZeitraumAsync(int tage)
    {
        aktiverZeitraum = tage.ToString();
        filterVon = Heute.AddDays(-(tage - 1));
        filterBis = Heute;
        await WendeFilterAsync();
    }

    private async Task WaehleMonatAsync()
    {
        aktiverZeitraum = "mois";
        filterVon = new DateTime(Heute.Year, Heute.Month, 1);
        filterBis = Heute;
        await WendeFilterAsync();
    }

    private async Task WendeEigenenZeitraumAsync()
    {
        aktiverZeitraum = "frei";
        await WendeFilterAsync();
    }

    // Ohne aktiven Filter gilt der Standard-Zeitraum der Umsatzliste (14 Tage).
    private DateTime StandardZeitraumVon() => ENote.Core.Services.AppUhr.UtcNow.Date.AddDays(-14);

    private void ExportierePdf()
    {
        var von = (filterVon ?? Heute).ToString("yyyy-MM-dd");
        var bis = (filterBis ?? Heute).ToString("yyyy-MM-dd");
        navigationManager.NavigateTo($"/finances/rapport?salonId={salonId}&von={von}&bis={bis}", forceLoad: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (hubVerbindung is not null)
        {
            try
            {
                await hubVerbindung.InvokeAsync("VerlasseSalon", UmsatzHub.SalonSchluessel(salonId));
                await hubVerbindung.DisposeAsync();
            }
            catch
            {
                // Beim Verlassen der Seite keine Fehler werfen.
            }
        }
    }
}
