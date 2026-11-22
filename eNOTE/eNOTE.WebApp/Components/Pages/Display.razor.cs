using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Components.Pages;

/// <summary>
/// Kunden-Display: zeigt das laufende Ticket eines Kassengeräts live per SignalR. Seit 22.11.2026 gilt nur der Link des Geräts,
/// an dem die Kasse zuletzt lief (<see cref="KundenDisplayLink"/>); ein alter Link zeigt « Ce lien n'est plus valide ».
/// Geprüft beim Öffnen, sofort bei « Reprendre la caisse ici » (<see cref="AnwesenheitsSignal.KassenGeraetGewechselt"/>), nach
/// einem Verbindungsabbruch und alle <see cref="KundenDisplayLink.PruefIntervall"/> (nächste Kasse auf einem anderen Gerät).
/// </summary>
public partial class Display(
    NavigationManager navigationManager,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SalonService salonService,
    AnwesenheitsSignal signal) : IAsyncDisposable
{
    // Ladenkopf (Name, Slogan, Logo) – nur Anzeigedaten, über die nicht erratbare Sitzungs-ID gefunden.
    private string? ladenName;
    private string? slogan;
    private string? logoUrl;

    private string LadenInitialen => string.IsNullOrWhiteSpace(ladenName)
        ? "eN"
        : string.Concat(ladenName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => char.IsLetter(t[0])).Take(2).Select(t => char.ToUpperInvariant(t[0])));

    /// <summary>Alter Link (bis 17.11.2026): Display je Kassensitzung – zeigt nur noch den Hinweis, neu zu verbinden.</summary>
    [Parameter] public Guid SitzungId { get; set; }

    /// <summary>Neuer Link: Laden (für Kopf/Logo) + Geräteschlüssel der Kasse, deren Tickets dieses Display zeigt.</summary>
    [Parameter] public Guid SalonId { get; set; }
    [Parameter] public string? Geraet { get; set; }

    /// <summary>Link gilt nicht (mehr): alter Link je Kassensitzung, ungültiger Schlüssel oder Kasse auf einem anderen Gerät.</summary>
    private bool ungueltig;

    private HubConnection? hubVerbindung;
    private bool verbunden;
    private DisplayTicket? ticket;
    private Timer? pruefTimer;
    private bool pruefungLaeuft;
    private bool beendet;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var salonId = Geraet is not null
                ? (Guid?)SalonId
                : await db.KassenSitzungen.AsNoTracking()
                    .Where(k => k.Id == SitzungId)
                    .Select(k => (Guid?)k.SalonId)
                    .FirstOrDefaultAsync();
            var salon = salonId is null ? null : await db.Salons.AsNoTracking()
                .Include(s => s.LogoAsset)
                .FirstOrDefaultAsync(s => s.Id == salonId);
            if (salon is not null)
            {
                ladenName = salon.Name;
                slogan = salon.Slogan;
                logoUrl = salonService.GetLogoUrl(salon);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Display-Kopf: {ex.Message}");
            // Kopfzeile ist Kosmetik – das Display funktioniert auch ohne.
        }

        // Alter Link oder ungültiger Schlüssel: nicht verbinden, nur Hinweis zeigen.
        if (Geraet is null || !UmsatzHub.IstGeraetSchluessel(Geraet))
        {
            ungueltig = true;
            return;
        }

        // Läuft die Kasse inzwischen auf einem anderen Gerät, gilt dieser Link nicht mehr.
        try
        {
            if (!await KundenDisplayLink.IstGueltigAsync(dbContextFactory, SalonId, Geraet))
            {
                ungueltig = true;
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Datenbank kurz nicht erreichbar: der Hub prüft beim Abonnieren erneut.
            Console.Error.WriteLine($"Display-Prüfung: {ex.Message}");
        }

        hubVerbindung = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/umsatz"))
            .WithAutomaticReconnect()
            .Build();

        hubVerbindung.On<DisplayTicket>("DisplayAktualisiert", t =>
        {
            if (ungueltig) return;
            ticket = t;
            InvokeAsync(StateHasChanged);
        });

        // Nach einem Verbindungsabbruch (WLAN, Standby) neu abonnieren – sonst bliebe das Display stumm.
        hubVerbindung.Reconnecting += _ =>
        {
            verbunden = false;
            return InvokeAsync(StateHasChanged);
        };
        hubVerbindung.Reconnected += async _ =>
        {
            await InvokeAsync(AbonniereAsync);
        };

        try
        {
            await hubVerbindung.StartAsync();
            await AbonniereAsync();
        }
        catch
        {
            verbunden = false;
        }
    }

    /// <summary>Gruppe des Kassengeräts abonnieren; der Hub lehnt einen nicht mehr gültigen Link ab.</summary>
    private async Task AbonniereAsync()
    {
        if (hubVerbindung is null || ungueltig) return;
        try
        {
            var gueltig = await hubVerbindung.InvokeAsync<bool>("AbonniereGeraetDisplay", SalonId.ToString(), Geraet);
            if (!gueltig)
            {
                await WerdeUngueltigAsync();
                return;
            }
            verbunden = true;
        }
        catch
        {
            verbunden = false;
        }
        StateHasChanged();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // Nur interaktiv (nicht beim Vorrendern): sofort bei « Reprendre la caisse ici » und regelmäßig prüfen.
        if (!firstRender || ungueltig || beendet) return;
        signal.KassenGeraetGewechselt += GeraetGewechselt;
        pruefTimer = new Timer(_ => PruefeSpaeter(), null, KundenDisplayLink.PruefIntervall, KundenDisplayLink.PruefIntervall);
    }

    private void GeraetGewechselt(Guid salonId)
    {
        if (salonId == SalonId) PruefeSpaeter();
    }

    /// <summary>Prüfung im Blazor-Kontext anstoßen (aus Timer oder Signal); nach dem Schließen der Seite nichts mehr.</summary>
    private void PruefeSpaeter()
    {
        if (beendet || ungueltig) return;
        try { _ = InvokeAsync(PruefeGueltigkeitAsync); }
        catch (ObjectDisposedException) { /* Seite schon geschlossen */ }
    }

    private async Task PruefeGueltigkeitAsync()
    {
        if (ungueltig || pruefungLaeuft || beendet) return;
        pruefungLaeuft = true;
        try
        {
            if (await KundenDisplayLink.IstGueltigAsync(dbContextFactory, SalonId, Geraet)) return;
        }
        catch
        {
            // Datenbank kurz weg: das Display läuft weiter, nächste Prüfung im nächsten Takt.
            return;
        }
        finally
        {
            pruefungLaeuft = false;
        }
        await WerdeUngueltigAsync();
    }

    /// <summary>Link gilt nicht mehr: Ticket ausblenden, Gruppe verlassen, Meldung zeigen.</summary>
    private async Task WerdeUngueltigAsync()
    {
        if (ungueltig) return;
        ungueltig = true;
        ticket = null;
        verbunden = false;
        StoppePruefung();
        await TrenneAsync();
        StateHasChanged();
    }

    private void StoppePruefung()
    {
        signal.KassenGeraetGewechselt -= GeraetGewechselt;
        pruefTimer?.Dispose();
        pruefTimer = null;
    }

    private async Task TrenneAsync()
    {
        var verbindung = hubVerbindung;
        hubVerbindung = null;
        if (verbindung is null) return;
        try
        {
            await verbindung.InvokeAsync("VerlasseGeraetDisplay", Geraet);
        }
        catch
        {
            // Beim Verlassen keine Fehler werfen.
        }
        try
        {
            await verbindung.DisposeAsync();
        }
        catch
        {
            // Beim Verlassen keine Fehler werfen.
        }
    }

    public async ValueTask DisposeAsync()
    {
        beendet = true;
        StoppePruefung();
        await TrenneAsync();
    }
}
