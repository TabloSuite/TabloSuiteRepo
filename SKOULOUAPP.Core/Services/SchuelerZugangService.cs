using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

/// <summary>Materialzugang ist eine Schul-/Klassenberechtigung, keine Zahlungsbestätigung.</summary>
public sealed class SchuelerZugangService(ISkoulouAppDbContext db)
{
    public async Task<HashSet<Guid>> KlassenAsync(Guid schuelerID, bool mitHistorie = false, CancellationToken token = default)
    {
        var freieSchulen = new SchulZugangService(db).FreigegebeneSchulen();
        var profile = await db.StudentProfiles.AsNoTracking()
            .Where(x => x.AppUserId == schuelerID && !x.IsArchived && !x.AppUser.IsBlocked && freieSchulen.Contains(x.SchoolId))
            .Select(x => new { x.SchoolId, x.CurrentKlasseId }).ToListAsync(token);
        if (profile.Count == 0) return [];

        var schulen = profile.Select(x => x.SchoolId).ToList();
        var klassen = profile.Where(x => x.CurrentKlasseId.HasValue).Select(x => x.CurrentKlasseId!.Value).ToHashSet();
        if (mitHistorie)
        {
            klassen.UnionWith(await db.LevelEnrollments.AsNoTracking()
                .Where(x => x.StudentId == schuelerID && x.DurchlaufId != null
                    && schulen.Contains(x.Durchlauf!.SchulID))
                .Select(x => x.KlasseId).ToListAsync(token));
            klassen.UnionWith(await db.KlassenAbschlussEntscheidungen.AsNoTracking()
                .Where(x => x.StudentId == schuelerID && schulen.Contains(x.Durchlauf.SchulID))
                .Select(x => x.Durchlauf.KlasseId).ToListAsync(token));
            // Bestehende, vor Einführung der Zyklus-ID dokumentierte Klassenzugehörigkeit.
            klassen.UnionWith(await db.LevelEnrollments.AsNoTracking()
                .Where(x => x.StudentId == schuelerID && x.DurchlaufId == null && (x.IsUnlocked || !x.IstAktuell))
                .Select(x => x.KlasseId).ToListAsync(token));
        }
        return await db.Klassen.AsNoTracking()
            .Where(x => klassen.Contains(x.Id) && x.SchoolId.HasValue && schulen.Contains(x.SchoolId.Value))
            .Select(x => x.Id).ToHashSetAsync(token);
    }
}
