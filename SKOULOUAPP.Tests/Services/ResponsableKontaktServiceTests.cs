using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>« Contact »: Die Direktion sieht den Responsable produit ihrer Schule – sonst niemand.</summary>
public class ResponsableKontaktServiceTests
{
    [Fact]
    public async Task Direktion_sieht_Responsable_ihrer_Schule_andere_nicht()
    {
        var factory = new TestApplicationDbContextFactory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École A" };
        var fremde = new School { Id = Guid.NewGuid(), Name = "École B" };
        Guid leiter, lehrer, fremderLeiter;
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Schools.AddRange(schule, fremde);
            var rollen = new[] { Rollen.SchulLeiter, Rollen.Lehrer, Rollen.ProduktOwner }
                .ToDictionary(n => n, n => new Role { Id = Guid.NewGuid(), Name = n });
            db.Roles.AddRange(rollen.Values);
            leiter = Konto("leitung", schule.Id, rollen[Rollen.SchulLeiter].Id);
            lehrer = Konto("lehrer", schule.Id, rollen[Rollen.Lehrer].Id);
            fremderLeiter = Konto("fremd", fremde.Id, rollen[Rollen.SchulLeiter].Id);
            Konto("Kossi Responsable", schule.Id, rollen[Rollen.ProduktOwner].Id, "+228 91 00 00 01");
            await db.SaveChangesAsync();

            Guid Konto(string name, Guid schulId, Guid rolle, string? telefon = null)
            {
                var user = new AppUser { Id = Guid.NewGuid(), UserName = name, FullName = name, Email = name.Replace(' ', '.') + "@test.local", TelefonNummer = telefon };
                db.Users.Add(user);
                db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = rolle });
                db.UserSchools.Add(new UserSchool { AppUserId = user.Id, SchoolId = schulId });
                return user.Id;
            }
        }
        var dienst = new ResponsableKontaktService(factory);

        var r = await dienst.GetFuerDirektionAsync(leiter);
        Assert.NotNull(r);
        Assert.Equal("Kossi Responsable", r.Name);
        Assert.Equal("Kossi.Responsable@test.local", r.Email);
        Assert.Equal("https://wa.me/22891000001", r.WhatsAppLink);

        Assert.Null(await dienst.GetFuerDirektionAsync(lehrer));
        Assert.Null(await dienst.GetFuerDirektionAsync(fremderLeiter));
    }
}
