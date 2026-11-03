using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using System.Security.Claims;

namespace ENote.WebApp.Components.Pages;

public partial class Finanzen(
    FinanzService finanzService,
    KatalogService katalogService,
    AuthenticationStateProvider authStateProvider,
    NavigationManager navigationManager) : IAsyncDisposable
{
    private bool isLoading = true;
    private bool darfFinanzenSehen;
    private Guid salonId;
    private string salonName = string.Empty;

    private List<FinanzService.TagesUmsatz> tagesUmsaetze = [];
    private List<FinanzService.VerkaufEintrag> letzteVerkaeufe = [];
    private List<Core.DataModels.Kasse.KassenSitzung> abschluesse = [];

    private HubConnection? hubVerbindung;
    private decimal liveTagesUmsatz;
    private int liveAnzahlHeute;
    private bool liveVerbunden;

    private DateTime filterVon = DateTime.Today.AddDays(-13);
    private DateTime filterBis = DateTime.Today;
    private bool filterAktiv;

    private string ZeitraumTitel => filterAktiv
        ? $"Chiffre d'affaires du {filterVon:dd.MM.yyyy} au {filterBis:dd.MM.yyyy}"
        : "Chiffre d'affaires (14 jours)";

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        var userIdValue = authState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            isLoading = false;
            return;
        }

        var zuordnung = await katalogService.GetUserSalonAsync(userId);
        if (zuordnung is null)
        {
            isLoading = false;
            return;
        }

        salonId = zuordnung.SalonId;
        salonName = zuordnung.Salon.Name;

        // Besitzer oder delegierter Kassierer mit Finanzrecht (serverseitig geprüft).
        var istBesitzer = authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Besitzer);
        darfFinanzenSehen = istBesitzer || zuordnung.KannFinanzenSehen;

        if (!darfFinanzenSehen)
        {
            isLoading = false;
            return;
        }

        await LadeAsync();
        await StarteLiveVerbindungAsync();
    }

    private async Task LadeAsync()
    {
        isLoading = true;
        tagesUmsaetze = await finanzService.GetTagesUmsaetzeAsync(salonId);
        letzteVerkaeufe = await finanzService.GetLetzteVerkaeufeAsync(salonId);
        abschluesse = await finanzService.GetAbschluesseAsync(salonId);

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
        filterAktiv = true;
        isLoading = true;
        var bisEnde = filterBis.Date.AddDays(1).AddTicks(-1);
        tagesUmsaetze = await finanzService.GetTagesUmsaetzeImZeitraumAsync(salonId, filterVon.Date, bisEnde);
        isLoading = false;
    }

    private void ExportierePdf()
    {
        var von = filterVon.ToString("yyyy-MM-dd");
        var bis = filterBis.ToString("yyyy-MM-dd");
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
