using System.Data;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class BesitzerBetreuungsService(IDbContextFactory<ApplicationDbContext> factory)
{
    public record BetreuungsUebersicht(Guid? ProduktOwnerId, string? Verantwortlicher, IReadOnlyList<string> Laeden);

    public async Task<BetreuungsUebersicht> GetUebersichtAsync(Guid adminId, Guid besitzerId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == adminId && !u.IsBlocked && db.UserRoles.Any(ur => ur.UserId == u.Id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.SuperAdmin)), ct))
            throw new UnauthorizedAccessException();
        var betreuung = await db.BesitzerBetreuungen.AsNoTracking()
            .SingleOrDefaultAsync(b => b.BesitzerId == besitzerId && b.GueltigBisUtc == null, ct);
        var name = betreuung is null ? null : await db.Users.Where(u => u.Id == betreuung.ProduktOwnerId).Select(u => u.FullName).SingleAsync(ct);
        var laeden = await db.UserSalons.AsNoTracking().Where(us => us.AppUserId == besitzerId)
            .OrderBy(us => us.Salon.Name).Select(us => us.Salon.Name).ToListAsync(ct);
        return new BetreuungsUebersicht(betreuung?.ProduktOwnerId, name, laeden);
    }

    public async Task WeiseAsync(Guid adminId, Guid besitzerId, Guid poId, CancellationToken ct = default)
    {
        await using var vor = await factory.CreateDbContextAsync(ct);
        await vor.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            async Task<bool> Rolle(Guid id, string rolle) => await db.Users.AnyAsync(u => u.Id == id && !u.IsBlocked
                && db.UserRoles.Any(ur => ur.UserId == id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == rolle)), ct);
            if (!await Rolle(adminId, AppRollen.SuperAdmin) || !await Rolle(besitzerId, AppRollen.Besitzer) || !await Rolle(poId, AppRollen.ProduktOwner))
                throw new UnauthorizedAccessException();
            var ids = await db.UserSalons.Where(us => us.AppUserId == besitzerId).Select(us => us.SalonId).ToListAsync(ct);
            var konflikte = await db.UserSalons.AnyAsync(us => ids.Contains(us.SalonId) && us.AppUserId != besitzerId
                && db.UserRoles.Any(ur => ur.UserId == us.AppUserId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == AppRollen.Besitzer)), ct);
            if (konflikte) throw new InvalidOperationException("Plusieurs propriétaires : vérifiez le titulaire du contrat avant l'affectation.");
            var jetzt = DateTime.UtcNow;
            var aktuell = await db.BesitzerBetreuungen.SingleOrDefaultAsync(b => b.BesitzerId == besitzerId && b.GueltigBisUtc == null, ct);
            if (aktuell?.ProduktOwnerId == poId) return;
            if (aktuell is not null) { aktuell.GueltigBisUtc = jetzt; await db.SaveChangesAsync(ct); }
            db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = besitzerId, ProduktOwnerId = poId, GueltigAbUtc = jetzt });
            var alte = await db.ProduktOwnerSalons.Where(p => ids.Contains(p.SalonId)).ToListAsync(ct);
            foreach (var alt in alte.Where(p => p.ProduktOwnerId != poId)) alt.GueltigBisUtc = jetzt;
            await db.SaveChangesAsync(ct);
            foreach (var id in ids.Where(id => !alte.Any(p => p.SalonId == id && p.ProduktOwnerId == poId)))
                db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { SalonId = id, ProduktOwnerId = poId, KannLadenUmsatzSehen = false, KannPlattformUmsatzSehen = false });
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        });
    }

    public static async Task ErgaenzeNeueLaedenAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var betreuungen = await db.BesitzerBetreuungen.Where(b => b.GueltigBisUtc == null).ToListAsync(ct);
        foreach (var b in betreuungen)
        {
            var ids = await db.UserSalons.Where(us => us.AppUserId == b.BesitzerId).Select(us => us.SalonId).ToListAsync(ct);
            foreach (var id in ids)
            {
                var vorhanden = await db.ProduktOwnerSalons.Where(p => p.SalonId == id).ToListAsync(ct);
                if (vorhanden.Any(p => p.ProduktOwnerId != b.ProduktOwnerId))
                    throw new InvalidOperationException("Conflit de responsable pour un établissement.");
                if (!vorhanden.Any()) db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { SalonId = id, ProduktOwnerId = b.ProduktOwnerId,
                    KannLadenUmsatzSehen = false, KannPlattformUmsatzSehen = false });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
