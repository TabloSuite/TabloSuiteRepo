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

    [Parameter] public Guid SitzungId { get; set; }

    private HubConnection? hubVerbindung;
    private bool verbunden;
    private DisplayTicket? ticket;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var salonId = await db.KassenSitzungen.AsNoTracking()
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

        hubVerbindung = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/umsatz"))
            .WithAutomaticReconnect()
            .Build();

        hubVerbindung.On<DisplayTicket>("DisplayAktualisiert", t =>
        {
            ticket = t;
            InvokeAsync(StateHasChanged);
        });

        try
        {
            await hubVerbindung.StartAsync();
            await hubVerbindung.InvokeAsync("AbonniereDisplay", SitzungId.ToString());
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
                await hubVerbindung.InvokeAsync("VerlasseDisplay", SitzungId.ToString());
                await hubVerbindung.DisposeAsync();
            }
            catch
            {
                // Beim Verlassen keine Fehler werfen.
            }
        }
    }
}
