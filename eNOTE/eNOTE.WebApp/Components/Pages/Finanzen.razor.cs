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
    AktiverSalonState aktiverSalon,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager,
    LadeZustand ladeZustand) : IAsyncDisposable
{
    private bool isLoading = true;
    private bool darfFinanzenSehen;
    private Guid salonId;
    private Guid benutzerId;
    private string salonName = string.Empty;

    private List<FinanzService.TagesUmsatz> tagesUmsaetze = [];
    private List<FinanzService.VerkaufEintrag> letzteVerkaeufe = [];
    private List<Core.DataModels.Kasse.KassenSitzung> abschluesse = [];
    private FinanzService.FinanzUebersicht? uebersicht;

    private HubConnection? hubVerbindung;
    private decimal liveTagesUmsatz;
    private int liveAnzahlHeute;
    private bool liveVerbunden;

    private DateTime? filterVon = DateTime.Today.AddDays(-13);
    private DateTime? filterBis = DateTime.Today;
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

    private IEnumerable<FinanzService.VerkaufEintrag> GefilterteVerkaeufe()
    {
        IEnumerable<FinanzService.VerkaufEintrag> q = letzteVerkaeufe;

        if (!string.IsNullOrWhiteSpace(verkaufsSuche))
        {
            var s = verkaufsSuche.Trim();
            q = q.Where(v =>
                v.PositionenText.Contains(s, StringComparison.OrdinalIgnoreCase)
                || v.KassiererName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        q = zahlungsArtFilter switch
        {
            "bar" => q.Where(v => v.Zahlungen.Any(z => z.Art == Core.DataModels.Kasse.ZahlungsArt.Bar)),
            "karte" => q.Where(v => v.Zahlungen.Any(z => z.Art == Core.DataModels.Kasse.ZahlungsArt.Karte)),
            "mobile" => q.Where(v => v.Zahlungen.Any(z => z.Art == Core.DataModels.Kasse.ZahlungsArt.MobileMoney)),
            _ => q
        };

        return q;
    }

    private string ZeitraumTitel => filterAktiv
        ? $"Chiffre d'affaires du {filterVon:dd.MM.yyyy} au {filterBis:dd.MM.yyyy}"
        : "Chiffre d'affaires (14 jours)";

    private string ZeitraumLabel => aktiverZeitraum switch
    {
        "7" => "7 derniers jours",
        "14" => "14 derniers jours",
        "30" => "30 derniers jours",
        "mois" => "ce mois",
        _ => $"du {filterVon:dd.MM} au {filterBis:dd.MM.yyyy}"
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
        tagesUmsaetze = await finanzService.GetTagesUmsaetzeAsync(salonId);
        letzteVerkaeufe = await finanzService.GetLetzteVerkaeufeAsync(salonId);
        abschluesse = await finanzService.GetAbschluesseAsync(salonId);
        uebersicht = await finanzService.GetFinanzUebersichtAsync(salonId, StandardZeitraumVon(), DateTime.MaxValue);
        bewegungen = await bestandsService.GetBewegungenAsync(salonId, StandardZeitraumVon(), DateTime.MaxValue);

        var heute = tagesUmsaetze.FirstOrDefault(t => t.Datum == DateTime.UtcNow.Date);
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
            await hubVerbindung.InvokeAsync("AbonniereSalon", salonId.ToString());
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
        var bisEnde = (filterBis ?? DateTime.Today).Date.AddDays(1).AddTicks(-1);
        tagesUmsaetze = await finanzService.GetTagesUmsaetzeImZeitraumAsync(salonId, (filterVon ?? DateTime.Today).Date, bisEnde);
        uebersicht = await finanzService.GetFinanzUebersichtAsync(salonId, (filterVon ?? DateTime.Today).Date, bisEnde);
        bewegungen = await bestandsService.GetBewegungenAsync(salonId, (filterVon ?? DateTime.Today).Date, bisEnde);
    }

    // --- Mouvements: Einlagen/Hilfen, Entnahmen, Warenabgänge ---
    private BestandsService.Bewegungen? bewegungen;
    private bool apportOffen;
    private decimal apportBetrag;
    private Core.DataModels.Finanzen.GeldOrt apportOrt = Core.DataModels.Finanzen.GeldOrt.AusserhalbKasse;
    private string? apportHerkunft;
    private string? apportNotiz;
    private DateTime? apportDatum = DateTime.Today;
    private string? apportFehler;
    private static readonly string[] HerkunftVorschlaege = ["Aide familiale", "Microcrédit / prêt", "Apport personnel", "Subvention", "Tontine"];

    private void OeffneApport()
    {
        apportBetrag = 0m;
        apportOrt = Core.DataModels.Finanzen.GeldOrt.AusserhalbKasse;
        apportHerkunft = null;
        apportNotiz = null;
        apportDatum = DateTime.Today;
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
        filterVon = DateTime.Today.AddDays(-(tage - 1));
        filterBis = DateTime.Today;
        await WendeFilterAsync();
    }

    private async Task WaehleMonatAsync()
    {
        aktiverZeitraum = "mois";
        filterVon = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        filterBis = DateTime.Today;
        await WendeFilterAsync();
    }

    private async Task WendeEigenenZeitraumAsync()
    {
        aktiverZeitraum = "frei";
        await WendeFilterAsync();
    }

    // Ohne aktiven Filter gilt der Standard-Zeitraum der Umsatzliste (14 Tage).
    private DateTime StandardZeitraumVon() => DateTime.UtcNow.Date.AddDays(-14);

    private void ExportierePdf()
    {
        var von = (filterVon ?? DateTime.Today).ToString("yyyy-MM-dd");
        var bis = (filterBis ?? DateTime.Today).ToString("yyyy-MM-dd");
        navigationManager.NavigateTo($"/finances/rapport?salonId={salonId}&von={von}&bis={bis}", forceLoad: true);
    }

    public async ValueTask DisposeAsync()
    {
        if (hubVerbindung is not null)
        {
            try
            {
                await hubVerbindung.InvokeAsync("VerlasseSalon", salonId.ToString());
                await hubVerbindung.DisposeAsync();
            }
            catch
            {
                // Beim Verlassen der Seite keine Fehler werfen.
            }
        }
    }
}
