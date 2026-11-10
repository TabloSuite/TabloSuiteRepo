using System.Globalization;
using System.Security.Claims;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Components.Pages;

public partial class Home(
    KatalogService katalogService,
    HinweisService hinweisService,
    DashboardService dashboardService,
    AktiverSalonState aktiverSalon,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    AuthenticationStateProvider authStateProvider,
    ILogger<Home> logger) : IDisposable
{
    private static readonly CultureInfo Francais = new("fr-FR");

    private bool wartetAufFreigabe;
    private string? salonNameWartend;
    private string? vorname;
    private string? ladenName;
    private bool darfZahlenSehen;
    private bool istBesitzer;
    private bool datenLaedt;
    private Guid benutzerId;
    private DashboardService.Daten? daten;
    private List<ENote.Core.DataModels.Kasse.BesitzerHinweis> ungeleseneHinweise = [];

    protected override async Task OnInitializedAsync()
    {
        var authState = await authStateProvider.GetAuthenticationStateAsync();
        if (!Guid.TryParse(authState.User.FindFirstValue(ClaimTypes.NameIdentifier), out benutzerId))
        {
            return;
        }

        istBesitzer = authState.User.IsInRole(Core.DataModels.Identity.AppRollen.Besitzer);

        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            vorname = await db.Users.Where(u => u.Id == benutzerId).Select(u => u.Vorname).FirstOrDefaultAsync();
        }

        var zuordnungen = await katalogService.GetUserSalonsAsync(benutzerId);
        if (zuordnungen.Count == 0)
        {
            return;
        }

        // Wartet nur, wenn es keinen freigegebenen Laden gibt, aber mindestens einen wartenden.
        if (!zuordnungen.Any(z => z.Salon.IstFreigegeben))
        {
            wartetAufFreigabe = true;
            salonNameWartend = zuordnungen.First().Salon.Name;
            return;
        }

        if (!aktiverSalon.IstGeladen)
        {
            await aktiverSalon.InitialisiereAsync(benutzerId);
        }

        aktiverSalon.OnChange += HandleSalonGewechselt;
        await LadeLadenDatenAsync();
    }

    private async Task LadeLadenDatenAsync()
    {
        var aktiv = aktiverSalon.Aktiv;
        if (aktiv is null)
        {
            return;
        }

        ladenName = aktiv.Salon.Name;
        // Hinweise (z. B. Kassenabweichungen) sind nur für den Besitzer bestimmt.
        ungeleseneHinweise = istBesitzer ? await hinweisService.GetUngeleseneAsync(aktiv.SalonId) : [];
        darfZahlenSehen = await dashboardService.DarfSehenAsync(benutzerId, aktiv.SalonId);
        if (!darfZahlenSehen)
        {
            daten = null;
            return;
        }

        datenLaedt = true;
        try
        {
            daten = await dashboardService.LadeAsync(benutzerId, aktiv.SalonId, DateTime.Now, TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            // Kennzahlen sind Komfort: ein Fehler darf die Startseite (Kasse öffnen, Hinweise) nie blockieren.
            logger.LogError(ex, "Dashboard für Laden {SalonId} konnte nicht geladen werden.", aktiv.SalonId);
            daten = null;
        }
        finally
        {
            datenLaedt = false;
        }
    }

    private void HandleSalonGewechselt() => _ = InvokeAsync(async () =>
    {
        await LadeLadenDatenAsync();
        StateHasChanged();
    });

    private async Task MarkiereGelesenAsync(Guid id)
    {
        await hinweisService.MarkiereGelesenAsync(id);
        ungeleseneHinweise.RemoveAll(h => h.Id == id);
    }

    private async Task MarkiereAlleGelesenAsync()
    {
        if (ungeleseneHinweise.Count == 0)
        {
            return;
        }

        await hinweisService.MarkiereAlleGelesenAsync(ungeleseneHinweise[0].SalonId);
        ungeleseneHinweise.Clear();
    }

    private static string Prozent(decimal wert, decimal max)
        => (max <= 0 ? 0 : Math.Round(wert / max * 100, 1)).ToString(CultureInfo.InvariantCulture);

    public void Dispose() => aktiverSalon.OnChange -= HandleSalonGewechselt;
}
