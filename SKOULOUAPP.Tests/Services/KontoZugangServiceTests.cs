using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Neues Startpasswort durch die Direktion: nur eigene Schule, nur verwaltbare Rollen, nie das eigene Konto.</summary>
public class KontoZugangServiceTests
{
    [Fact]
    public async Task Direktion_Setzt_Neues_Passwort_Nur_Fuer_Eigene_Schueler_Und_Lehrkraefte()
    {
        var factory = new TestApplicationDbContextFactory();
        var schule = Guid.NewGuid(); var fremdeSchule = Guid.NewGuid();
        var direktor = new AppUser { Id = Guid.NewGuid(), UserName = "direction" };
        var schueler = new AppUser { Id = Guid.NewGuid(), UserName = "esi.adjo", FullName = "Esi Adjo" };
        var lehrer = new AppUser { Id = Guid.NewGuid(), UserName = "kodjo.agbeko", FullName = "Kodjo Agbeko" };
        var fremd = new AppUser { Id = Guid.NewGuid(), UserName = "yao.autre", FullName = "Yao Autre" };
        await using (var db = factory.CreateDbContext())
        {
            db.Schools.AddRange(new School { Id = schule, Name = "Collège Démo", City = "Lomé" }, new School { Id = fremdeSchule, Name = "Autre" });
            db.Users.AddRange(direktor, schueler, lehrer, fremd);
            db.UserSchools.AddRange(
                new UserSchool { AppUserId = direktor.Id, SchoolId = schule, CanManageStudents = true },
                new UserSchool { AppUserId = schueler.Id, SchoolId = schule },
                new UserSchool { AppUserId = lehrer.Id, SchoolId = schule },
                new UserSchool { AppUserId = fremd.Id, SchoolId = fremdeSchule });
            await db.SaveChangesAsync();
        }

        var manager = TestUserManager.Create([direktor, schueler, lehrer, fremd]);
        manager.SeedRole(direktor, Rollen.SchulLeiter);
        manager.SeedRole(schueler, Rollen.Schueler);
        manager.SeedRole(lehrer, Rollen.Lehrer);
        manager.SeedRole(fremd, Rollen.Schueler);
        var service = new KontoZugangService(new DashboardService(factory.CreateDbContext(), manager, new FakeMediaStorageService()), manager);

        var sitzungVorher = schueler.ActiveSessionId;
        var zugang = await service.NeuesPasswortAsync(direktor.Id, schueler.Id);
        Assert.Equal("esi.adjo", zugang.Identifiant);
        Assert.False(string.IsNullOrWhiteSpace(zugang.Passwort));
        Assert.NotEqual(sitzungVorher, schueler.ActiveSessionId);

        Assert.Equal("kodjo.agbeko", (await service.NeuesPasswortAsync(direktor.Id, lehrer.Id)).Identifiant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NeuesPasswortAsync(direktor.Id, fremd.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NeuesPasswortAsync(direktor.Id, direktor.Id));
    }
}
