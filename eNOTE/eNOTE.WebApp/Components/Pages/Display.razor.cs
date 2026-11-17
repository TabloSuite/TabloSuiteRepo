using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Components.Pages;

/// <summary>Kunden-Display: zeigt das laufende Ticket einer Kassensitzung live per SignalR.</summary>
public partial class Display(
    NavigationManager navigationManager,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    SalonService salonService) : IAsyncDisposable
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

    private bool veralteterLink;

    private HubConnection? hubVerbindung;
    private bool verbunden;
    private DisplayTicket? ticket;

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
            veralteterLink = true;
            return;
        }

        hubVerbindung = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/umsatz"))
            .WithAutomaticReconnect()
            .Build();

        hubVerbindung.On<DisplayTicket>("DisplayAktualisiert", t =>
        {
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
            try
            {
                await hubVerbindung.InvokeAsync("AbonniereGeraetDisplay", Geraet);
                verbunden = true;
            }
            catch
            {
                verbunden = false;
            }
            await InvokeAsync(StateHasChanged);
        };

        try
        {
            await hubVerbindung.StartAsync();
            await hubVerbindung.InvokeAsync("AbonniereGeraetDisplay", Geraet);
            verbunden = true;
        }
        catch
        {
            verbunden = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (hubVerbindung is not null)
        {
            try
            {
                await hubVerbindung.InvokeAsync("VerlasseGeraetDisplay", Geraet);
                await hubVerbindung.DisposeAsync();
            }
            catch
            {
                // Beim Verlassen keine Fehler werfen.
            }
        }
    }
}
