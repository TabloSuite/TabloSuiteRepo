using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Gesprochene Rückmeldung bei Übungen (« Bravo ! », « Oups, essaie encore ! »): pro Schülerkonto
/// abschaltbar. Gespeichert am Schülerprofil (<c>AntwortTonAus</c>), damit die Wahl auf jedem Gerät gilt.
/// </summary>
public sealed class AntwortTonService(ISkoulouAppDbContext db)
{
    public Task<bool> IstAusAsync(Guid schuelerId, CancellationToken token = default)
        => db.StudentProfiles.AsNoTracking().AnyAsync(profil => profil.AppUserId == schuelerId && profil.AntwortTonAus, token);

    /// <summary>Setzt den Schalter für alle Profile des Kontos. false = kein Schülerprofil vorhanden.</summary>
    public async Task<bool> SetzeAsync(Guid schuelerId, bool aus, CancellationToken token = default)
    {
        var profile = await db.StudentProfiles.Where(profil => profil.AppUserId == schuelerId).ToListAsync(token);
        if (profile.Count == 0)
        {
            return false;
        }

        foreach (var profil in profile)
        {
            profil.AntwortTonAus = aus;
        }

        await db.SaveChangesAsync(token);
        return true;
    }
}
