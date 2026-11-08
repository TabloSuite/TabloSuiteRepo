using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

public class BesitzerBetreuungTests
{
    [Fact]
    public async Task Uebergabe_Alle_Laeden_Entzieht_Alte_Rechte_Und_Erhaelt_Provision()
    {
        var factory = TestDbContextFactory.Create();
        var admin = new AppUser(); var owner = new AppUser(); var alt = new AppUser(); var neu = new AppUser();
        var a = new Salon { Name = "A" }; var b = new Salon { Name = "B" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            foreach (var (user, rolle) in new[] { (admin, AppRollen.SuperAdmin), (owner, AppRollen.Besitzer), (alt, AppRollen.ProduktOwner), (neu, AppRollen.ProduktOwner) })
            {
                var r = await db.Roles.FirstOrDefaultAsync(r => r.Name == rolle);
                if (r is null) { r = new Role { Name = rolle }; db.Roles.Add(r); }
                db.Users.Add(user); db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = r.Id });
                await db.SaveChangesAsync();
            }
            db.UserSalons.AddRange(new UserSalon { AppUserId = owner.Id, Salon = a }, new UserSalon { AppUserId = owner.Id, Salon = b });
            db.ProduktOwnerSalons.Add(new ProduktOwnerSalon { ProduktOwnerId = alt.Id, Salon = a, ProzentProvision = 10, CreatedAtUtc = new DateTime(2020, 1, 1) });
            db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = owner.Id, ProduktOwnerId = alt.Id });
            db.NutzungsAbos.Add(new NutzungsAbo { Salon = a, PreisSnapshot = 5000, ZahlungStatus = AboZahlungStatus.Bestaetigt, ZahlungBestaetigtUtc = new DateTime(2026, 1, 5) });
            await db.SaveChangesAsync();
        }
        var service = new BesitzerBetreuungsService(factory);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetUebersichtAsync(neu.Id, owner.Id));
        var vorher = await service.GetUebersichtAsync(admin.Id, owner.Id);
        Assert.Equal(alt.Id, vorher.ProduktOwnerId);
        Assert.Equal(new[] { "A", "B" }, vorher.Laeden);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.WeiseAsync(neu.Id, owner.Id, neu.Id));
        await service.WeiseAsync(admin.Id, owner.Id, neu.Id);
        await service.WeiseAsync(admin.Id, owner.Id, neu.Id);
        Assert.Equal(neu.Id, (await service.GetUebersichtAsync(admin.Id, owner.Id)).ProduktOwnerId);
        await using (var db = await factory.CreateDbContextAsync())
        {
            // Alte Zuordnungen des bisherigen PO sind archiviert (GueltigBisUtc gesetzt), nicht gelöscht.
            var altZuordnungen = await db.ProduktOwnerSalons.Where(p => p.ProduktOwnerId == alt.Id).ToListAsync();
            Assert.All(altZuordnungen, p => Assert.NotNull(p.GueltigBisUtc));
            // Aktuell aktiv sind nur die Zuordnungen des neuen PO (ohne Umsatzrechte).
            var aktuell = await db.ProduktOwnerSalons.Where(p => p.GueltigBisUtc == null).ToListAsync();
            Assert.Equal(2, aktuell.Count);
            Assert.All(aktuell, p => { Assert.Equal(neu.Id, p.ProduktOwnerId); Assert.False(p.KannLadenUmsatzSehen); Assert.False(p.KannPlattformUmsatzSehen); });
            Assert.Equal(3, await db.ProduktOwnerSalons.IgnoreQueryFilters().CountAsync());
            Assert.Equal(2, await db.BesitzerBetreuungen.CountAsync());
        }
        var provision = await new ProduktOwnerService(factory).BerechneAuszahlungAsync(alt.Id, 2026, 1);
        Assert.Equal(500, provision.Betrag);
        Assert.Equal(0, (await new ProduktOwnerService(factory).BerechneAuszahlungAsync(neu.Id, 2026, 1)).Betrag);
    }
}
