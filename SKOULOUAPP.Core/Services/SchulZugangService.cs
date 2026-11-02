using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

/// <summary>Schulsperren werden abgeleitet, niemals in persönliche Nutzerflags kopiert.</summary>
public sealed class SchulZugangService(ISkoulouAppDbContext db)
{
    public const string GesperrtHinweis = "L'accès à votre école est suspendu. Veuillez contacter la direction de votre établissement.";

    public IQueryable<Guid> FreigegebeneSchulen()
        => db.Schools.AsNoTracking().Where(s => s.IsActive
            && !db.UserSchools.Any(link => link.SchoolId == s.Id && link.AppUser.IsBlocked
                && db.UserRoles.Any(ur => ur.UserId == link.AppUserId
                    && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Rollen.SchulLeiter))))
            .Select(s => s.Id);

    public Task<bool> IstSchuleFreigegebenAsync(Guid schulID, CancellationToken token = default)
        => FreigegebeneSchulen().AnyAsync(id => id == schulID, token);

    public async Task<bool> DarfAnmeldenAsync(Guid userID, CancellationToken token = default)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == userID && !u.IsBlocked, token)) return false;
        if (await db.UserRoles.AnyAsync(ur => ur.UserId == userID && db.Roles.Any(r => r.Id == ur.RoleId
            && (r.Name == Rollen.SuperAdmin || r.Name == Rollen.ProduktOwner)), token)) return true;
        var zugeordnet = db.UserSchools.Where(x => x.AppUserId == userID).Select(x => x.SchoolId)
            .Union(db.StudentProfiles.Where(x => x.AppUserId == userID && !x.IsArchived).Select(x => x.SchoolId));
        if (!await zugeordnet.AnyAsync(token)) return true; // Registrierung/Profil ohne Schule bleibt erreichbar.
        var frei = FreigegebeneSchulen();
        return await zugeordnet.AnyAsync(id => frei.Contains(id), token);
    }
}
