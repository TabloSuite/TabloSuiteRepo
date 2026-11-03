using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

/// <summary>SuperAdmin-Ansicht der zentralen Exception-Logs.</summary>
public class ExceptionLogService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<List<ExceptionLog>> GetLogsAsync(
        DateTime? von,
        DateTime? bis,
        Guid? salonId,
        bool nurUnbehandelt,
        int anzahl = 100,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ExceptionLogs.AsNoTracking().AsQueryable();

        if (von is not null) query = query.Where(l => l.ZeitUtc >= von);
        if (bis is not null) query = query.Where(l => l.ZeitUtc <= bis);
        if (salonId is not null) query = query.Where(l => l.SalonId == salonId);
        if (nurUnbehandelt) query = query.Where(l => !l.IstBehandelt);

        return await query.OrderByDescending(l => l.ZeitUtc).Take(anzahl).ToListAsync(cancellationToken);
    }

    public async Task<bool> MarkiereBehandeltAsync(Guid id, bool behandelt, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var log = await db.ExceptionLogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (log is null)
        {
            return false;
        }

        log.IstBehandelt = behandelt;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> LoescheAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var log = await db.ExceptionLogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (log is null)
        {
            return false;
        }

        db.ExceptionLogs.Remove(log);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> LoescheBehandelteAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ExceptionLogs.Where(l => l.IstBehandelt).ExecuteDeleteAsync(cancellationToken);
    }
}
