using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace ENote.WebApp.Services;

/// <summary>
/// Circuit-weiter Zustand des aktuell gewählten Ladens (für Besitzer/Kassierer mit
/// mehreren Läden). Wird geräteweit im LocalStorage gehalten, damit nach Reload der
/// zuletzt gewählte Laden erhalten bleibt. Steuert alle Verwaltungsseiten pro Laden.
/// </summary>
public class AktiverSalonState(IDbContextFactory<ApplicationDbContext> dbContextFactory, IJSRuntime js)
{
    private const string StorageKey = "enote_aktiver_salon";

    public event Action? OnChange;

    /// <summary>Alle dem Nutzer zugeordneten Läden (für den Umschalter).</summary>
    public List<UserSalon> Laeden { get; private set; } = [];

    /// <summary>Aktuell aktiver Laden (null, solange nicht geladen).</summary>
    public UserSalon? Aktiv { get; private set; }

    public bool IstGeladen { get; private set; }

    /// <summary>Lädt die Läden des Nutzers und stellt den gespeicherten (oder ersten freigegebenen) Laden ein.</summary>
    public async Task InitialisiereAsync(Guid benutzerId)
    {
        Laeden = await LadeLaedenAsync(benutzerId);

        Guid? gespeichert = null;
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (Guid.TryParse(raw, out var g))
            {
                gespeichert = g;
            }
        }
        catch (InvalidOperationException)
        {
            // JS noch nicht verfügbar (Prerendering) – Fallback unten.
        }

        Aktiv = Laeden.FirstOrDefault(l => l.SalonId == gespeichert)
            ?? Laeden.FirstOrDefault(l => l.Salon.IstFreigegeben)
            ?? Laeden.FirstOrDefault();

        IstGeladen = true;
        OnChange?.Invoke();
    }

    /// <summary>Wechselt den aktiven Laden und speichert ihn geräteweit.</summary>
    public async Task WaehleAsync(Guid salonId)
    {
        var ziel = Laeden.FirstOrDefault(l => l.SalonId == salonId);
        if (ziel is null || Aktiv?.SalonId == salonId)
        {
            return;
        }

        Aktiv = ziel;
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, salonId.ToString());
        }
        catch (InvalidOperationException)
        {
            // Prerendering – wird beim nächsten interaktiven Render erneut gesetzt.
        }

        OnChange?.Invoke();
    }

    /// <summary>Lädt die Läden neu (z. B. nach dem Anlegen eines weiteren Ladens).</summary>
    public async Task AktualisiereLaedenAsync(Guid benutzerId)
    {
        var aktivId = Aktiv?.SalonId;
        Laeden = await LadeLaedenAsync(benutzerId);
        Aktiv = Laeden.FirstOrDefault(l => l.SalonId == aktivId)
            ?? Laeden.FirstOrDefault(l => l.Salon.IstFreigegeben)
            ?? Laeden.FirstOrDefault();
        OnChange?.Invoke();
    }

    private async Task<List<UserSalon>> LadeLaedenAsync(Guid benutzerId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.UserSalons
            .Include(us => us.Salon)
            .AsNoTracking()
            .Where(us => us.AppUserId == benutzerId)
            .OrderBy(us => us.Salon.Name)
            .ToListAsync();
    }
}
