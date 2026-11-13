using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Zähler der Startseite (aktive Schulen, eingeschriebene Schüler), 10 Minuten zwischengespeichert.</summary>
public sealed class StartseitenStatistik(IDbContextFactory<SkoulouDbContext> dbFactory, IMemoryCache cache, ILogger<StartseitenStatistik> logger)
{
    public sealed record Zahlen(int Schulen, int Schueler);

    /// <summary>Zahlen oder null (bei einem Fehler blendet die Startseite sie aus).</summary>
    public async Task<Zahlen?> LadeAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue("startseite:zahlen", out Zahlen? zahlen)) return zahlen;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var schulen = await db.Schulen.CountAsync(s => s.Status == SchulStatus.Active, ct);
            var schueler = await (from e in db.Einschreibungen
                                  join s in db.Schulen on e.SchuleId equals s.Id
                                  where e.Status == EinschreibungsStatus.Active && s.Status == SchulStatus.Active
                                  select e.SchuelerId).Distinct().CountAsync(ct);
            zahlen = new Zahlen(schulen, schueler);
            cache.Set("startseite:zahlen", zahlen, TimeSpan.FromMinutes(10));
            return zahlen;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Zähler der Startseite nicht verfügbar: {Meldung}", ex.Message);
            return null;
        }
    }
}
