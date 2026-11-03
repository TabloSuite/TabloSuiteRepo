using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace ENote.WebApp.Services;

/// <summary>
/// Verwaltet den optionalen Geräte-Favoriten: ein stabiler, zufälliger Schlüssel
/// liegt im LocalStorage des Tablets und wird einem Salon zugeordnet. Dient nur
/// der Vorauswahl beim Kassenstart; die fachliche Bindung bleibt die Kassen-PIN.
/// </summary>
public class GeraetFavoritService(IDbContextFactory<ApplicationDbContext> dbContextFactory, IJSRuntime jsRuntime)
{
    private const string StorageKey = "enote_geraet_schluessel";

    /// <summary>Liefert den stabilen Geräteschlüssel (legt ihn bei Bedarf an).</summary>
    public async Task<string> GetGeraeteSchluesselAsync()
    {
        var schluessel = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrWhiteSpace(schluessel))
        {
            schluessel = Guid.NewGuid().ToString("N");
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, schluessel);
        }

        return schluessel;
    }

    /// <summary>Der für dieses Gerät hinterlegte Favoriten-Salon (falls vorhanden).</summary>
    public async Task<Guid?> GetFavoritSalonIdAsync(CancellationToken cancellationToken = default)
    {
        string schluessel;
        try
        {
            schluessel = await GetGeraeteSchluesselAsync();
        }
        catch (InvalidOperationException)
        {
            // JS nicht verfügbar (z. B. Prerendering).
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.GeraetSalonFavoriten
            .AsNoTracking()
            .Where(g => g.GeraeteSchluessel == schluessel)
            .Select(g => (Guid?)g.SalonId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Setzt oder ersetzt den Favoriten für dieses Gerät.</summary>
    public async Task SetzeFavoritAsync(Guid salonId, string? geraeteName, CancellationToken cancellationToken = default)
    {
        var schluessel = await GetGeraeteSchluesselAsync();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var vorhanden = await db.GeraetSalonFavoriten
            .FirstOrDefaultAsync(g => g.GeraeteSchluessel == schluessel, cancellationToken);

        if (vorhanden is null)
        {
            db.GeraetSalonFavoriten.Add(new Core.DataModels.Salons.GeraetSalonFavorit
            {
                GeraeteSchluessel = schluessel,
                SalonId = salonId,
                GeraeteName = string.IsNullOrWhiteSpace(geraeteName) ? null : geraeteName.Trim()
            });
        }
        else
        {
            vorhanden.SalonId = salonId;
            if (!string.IsNullOrWhiteSpace(geraeteName))
            {
                vorhanden.GeraeteName = geraeteName.Trim();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Entfernt den Favoriten dieses Geräts.</summary>
    public async Task EntferneFavoritAsync(CancellationToken cancellationToken = default)
    {
        var schluessel = await GetGeraeteSchluesselAsync();

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var vorhanden = await db.GeraetSalonFavoriten
            .FirstOrDefaultAsync(g => g.GeraeteSchluessel == schluessel, cancellationToken);
        if (vorhanden is not null)
        {
            db.GeraetSalonFavoriten.Remove(vorhanden);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
