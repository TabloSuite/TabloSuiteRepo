using ENote.Core.DataModels.Kasse;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>Ungelesene Hinweise für den Besitzer (z. B. Standard-Artikel „Divers“ verwendet).</summary>
public class HinweisService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    /// <summary>Ungelesene Hinweise eines Ladens (neueste zuerst).</summary>
    public async Task<List<BesitzerHinweis>> GetUngeleseneAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BesitzerHinweise
            .AsNoTracking()
            .Where(h => h.SalonId == salonId && h.IstUngelesen)
            .OrderByDescending(h => h.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Anzahl ungelesener Hinweise (für den roten Badge).</summary>
    public async Task<int> GetAnzahlUngelesenAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BesitzerHinweise
            .CountAsync(h => h.SalonId == salonId && h.IstUngelesen, cancellationToken);
    }

    /// <summary>Markiert einen Hinweis als gelesen.</summary>
    public async Task<bool> MarkiereGelesenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var hinweis = await db.BesitzerHinweise.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (hinweis is null)
        {
            return false;
        }

        hinweis.IstUngelesen = false;
        hinweis.GelesenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Markiert alle Hinweise eines Ladens als gelesen.</summary>
    public async Task<int> MarkiereAlleGelesenAsync(Guid salonId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.BesitzerHinweise
            .Where(h => h.SalonId == salonId && h.IstUngelesen)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.IstUngelesen, false).SetProperty(h => h.GelesenUtc, DateTime.UtcNow), cancellationToken);
    }
}
