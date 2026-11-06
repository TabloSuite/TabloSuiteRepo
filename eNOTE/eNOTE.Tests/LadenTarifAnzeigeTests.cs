using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

public class LadenTarifAnzeigeTests
{
    [Fact]
    public async Task Aktueller_Tarif_Enthaelt_Globalen_Und_Individuellen_Preis_Und_Prueft_Rechte()
    {
        var factory = TestDbContextFactory.Create();
        var admin = new AppUser { Id = Guid.NewGuid() };
        var salon = new Salon { Name = "Boutique A", IndividuellerMonatsPreis = 7500 };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.SuperAdmin };
            db.Users.Add(admin); db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.PlattformPreise.AddRange(
                new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 4000, IstAktuell = false },
                new PlattformPreis { Intervall = AboIntervall.Monatlich, Preis = 5000, IstAktuell = true });
            await db.SaveChangesAsync();
        }
        var service = new AdminService(factory, null!, NullLogger<AdminService>.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetLadenTarifAsync(Guid.NewGuid(), salon.Id));
        var vorher = await service.GetLadenTarifAsync(admin.Id, salon.Id);
        Assert.Equal("Boutique A", vorher.Name);
        Assert.Equal(5000, vorher.Global);
        Assert.Equal(7500, vorher.Individuell);
        Assert.True(await service.SetzeIndividuellenPreisAsync(admin.Id, salon.Id, null));
        var nachher = await service.GetLadenTarifAsync(admin.Id, salon.Id);
        Assert.Null(nachher.Individuell);
        Assert.Equal(5000, nachher.Global);
    }
}
