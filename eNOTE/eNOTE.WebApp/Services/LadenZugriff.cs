using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public static class LadenZugriff
{
    public static Task<bool> HatZugangAsync(ApplicationDbContext db, Guid benutzerId, Guid salonId, CancellationToken ct = default)
        => db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.SalonId == salonId
            && !us.AppUser.IsBlocked && us.Salon.IstAktiv, ct);

    public static Task<bool> IstBesitzerAsync(ApplicationDbContext db, Guid benutzerId, Guid salonId, CancellationToken ct = default)
        => db.UserSalons.AnyAsync(us => us.AppUserId == benutzerId && us.SalonId == salonId
            && !us.AppUser.IsBlocked && us.Salon.IstAktiv && db.UserRoles.Any(ur => ur.UserId == benutzerId
                && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)), ct);
}
