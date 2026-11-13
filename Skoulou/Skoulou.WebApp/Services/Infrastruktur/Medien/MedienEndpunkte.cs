using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur.Medien;

/// <summary>
/// Geprüfte Auslieferung privater Medien (<c>/medien/{id}</c>). E1: Gerüst mit Grundregeln –
/// Admin alles, Besitzer, Personal derselben Schule; Schüler/Eltern nur über eine Nutzung
/// (wird mit den Inhalten in E4 verfeinert).
/// </summary>
public static class MedienEndpunkte
{
    public static IEndpointRouteBuilder MapMedienEndpunkte(this IEndpointRouteBuilder app)
    {
        app.MapGet("/medien/{id:guid}", async (Guid id, HttpContext http, IDbContextFactory<SkoulouDbContext> dbFactory,
            IMedienSpeicher speicher, IEnumerable<IMedienFreigabe> freigaben, CancellationToken ct) =>
        {
            var kontoId = http.User.KontoId();
            if (kontoId is null) return Results.Unauthorized();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var medium = await db.Medien.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
            if (medium is null) return Results.NotFound();

            var konto = await db.Users.AsNoTracking().Where(k => k.Id == kontoId)
                .Select(k => new { k.Rolle, k.SchuleId, k.Status }).FirstOrDefaultAsync(ct);
            if (konto is null || konto.Status != KontoStatus.Actif) return Results.NotFound();
            if (!DarfSehen(konto.Rolle, konto.SchuleId, kontoId.Value, medium))
            {
                var erlaubt = false;
                foreach (var freigabe in freigaben)
                    if (erlaubt = await freigabe.DarfSehenAsync(db, kontoId.Value, konto.Rolle, konto.SchuleId, medium, ct)) break;
                if (!erlaubt) return Results.NotFound();
            }

            var strom = await speicher.OeffnenAsync(medium.BlobName, ct);
            if (strom is null) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=3600";
            http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Results.Stream(strom, medium.ContentType, enableRangeProcessing: strom.CanSeek);
        }).RequireAuthorization();
        return app;
    }

    /// <summary>Grundregel der Sichtbarkeit (ohne Nutzungsprüfung).</summary>
    public static bool DarfSehen(string rolle, Guid? schuleId, Guid kontoId, Medium medium)
        => rolle == Rollen.SuperAdmin
           || medium.BesitzerId == kontoId
           || (medium.SchuleId is not null && medium.SchuleId == schuleId && Rollen.Lehrkraefte.Contains(rolle));
}
