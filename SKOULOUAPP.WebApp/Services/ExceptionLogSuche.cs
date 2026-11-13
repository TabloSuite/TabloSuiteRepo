using Microsoft.EntityFrameworkCore;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

/// <summary>Filter des Fehlerjournals (Datum von/bis inklusive, Schule, nur offene, Suchtext).</summary>
public sealed record ExceptionLogFilter(DateTime? Von = null, DateTime? Bis = null, Guid? SchulId = null, bool NurOffene = false, string? Suche = null);

/// <summary>
/// Serverseitige Suche im Fehlerjournal: Filter + Sortierung (neueste zuerst) + Skip/Take,
/// damit nie das ganze Journal geladen wird.
/// </summary>
public static class ExceptionLogSuche
{
    public static async Task<(List<ExceptionLog> Eintraege, bool Mehr)> SucheAsync(ApplicationDbContext db, ExceptionLogFilter filter,
        int ueberspringen, int anzahl = 50, CancellationToken cancellationToken = default)
    {
        var query = db.ExceptionLogs.AsNoTracking().Include(log => log.School).AsQueryable();
        if (filter.Von.HasValue)
        {
            var von = filter.Von.Value.Date;
            query = query.Where(log => log.OccurredAtUtc >= von);
        }

        if (filter.Bis.HasValue)
        {
            var bis = filter.Bis.Value.Date.AddDays(1);
            query = query.Where(log => log.OccurredAtUtc < bis);
        }

        if (filter.SchulId.HasValue)
        {
            query = query.Where(log => log.SchoolId == filter.SchulId);
        }

        if (filter.NurOffene)
        {
            query = query.Where(log => !log.IsHandled);
        }

        if (!string.IsNullOrWhiteSpace(filter.Suche))
        {
            var s = filter.Suche.Trim();
            query = query.Where(log => log.Message.Contains(s)
                || (log.ExceptionType != null && log.ExceptionType.Contains(s))
                || (log.RequestPath != null && log.RequestPath.Contains(s)));
        }

        anzahl = Math.Clamp(anzahl, 1, 200);
        var liste = await query.OrderByDescending(log => log.OccurredAtUtc).ThenBy(log => log.Id)
            .Skip(Math.Max(0, ueberspringen)).Take(anzahl + 1).ToListAsync(cancellationToken);
        var mehr = liste.Count > anzahl;
        if (mehr) liste.RemoveAt(liste.Count - 1);
        return (liste, mehr);
    }
}
