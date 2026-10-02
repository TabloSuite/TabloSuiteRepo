using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class AbrechnungBenachrichtigungServiceTests
{
    private static AbrechnungBenachrichtigungService Create(TestApplicationDbContextFactory factory, FakeEmailService email)
        => new(factory, email, NullLogger<AbrechnungBenachrichtigungService>.Instance);

    [Fact]
    public async Task ManuelleRechnungSendenAsync_NurAktiveLeiterDerRechnungsschule_MitBetrag()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var schule = new School { Id = Guid.NewGuid(), Name = "École A" };
        var fremdeSchule = new School { Id = Guid.NewGuid(), Name = "École B" };
        var periode = new Abrechnungsperiode
        {
            Id = Guid.NewGuid(), SchulID = schule.Id, AnzahlAbonnements = 2, Umsatz = 22000m,
            VonUtc = new DateTime(2026, 1, 1), BisUtc = new DateTime(2026, 2, 1)
        };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Schools.AddRange(schule, fremdeSchule);
            db.Abrechnungsperioden.Add(periode);
            var leiterRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer };
            db.Roles.AddRange(leiterRolle, lehrerRolle);
            AddUser("leitung@a.org", schule.Id, leiterRolle.Id);
            AddUser("fremd@b.org", fremdeSchule.Id, leiterRolle.Id);
            AddUser("lehrer@a.org", schule.Id, lehrerRolle.Id);
            AddUser("gesperrt@a.org", schule.Id, leiterRolle.Id, blocked: true);
            await db.SaveChangesAsync();

            void AddUser(string adresse, Guid schulId, Guid rollenId, bool blocked = false)
            {
                var user = new AppUser { Id = Guid.NewGuid(), UserName = adresse, Email = adresse, Vorname = "Afi", IsBlocked = blocked };
                db.Users.Add(user);
                db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = rollenId });
                db.UserSchools.Add(new UserSchool { AppUserId = user.Id, SchoolId = schulId, CanManageStudents = true });
            }
        }

        var result = await Create(factory, email).ManuelleRechnungSendenAsync(periode.Id);

        Assert.Equal((1, 0), result);
        var sent = Assert.Single(email.Gesendet);
        Assert.Equal("leitung@a.org", sent.Recipient.Email);
        Assert.Contains("École A", System.Net.WebUtility.HtmlDecode(sent.Message.HtmlBody));
        Assert.Contains("22", sent.Message.HtmlBody);
        Assert.Contains("2", sent.Message.HtmlBody);
        Assert.Contains("virement", sent.Message.HtmlBody);
        Assert.Contains("SKOULOUAPP", sent.Message.HtmlBody);
        Assert.False(string.IsNullOrWhiteSpace(sent.Message.TextBody));
    }

    [Fact]
    public async Task ManuelleRechnungSendenAsync_AutomatischeRechnungOderFehlendeEmpfaenger_KeinVersand()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var schule = new School { Id = Guid.NewGuid(), Name = "École A" };
        var automatisch = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id, AutomatischDurchKlassenabschlussErstellt = true };
        var manuell = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Schools.Add(schule);
            db.Abrechnungsperioden.AddRange(automatisch, manuell);
            await db.SaveChangesAsync();
        }
        var service = Create(factory, email);

        Assert.Equal((0, 0), await service.ManuelleRechnungSendenAsync(automatisch.Id));
        Assert.Equal((0, 1), await service.ManuelleRechnungSendenAsync(manuell.Id));
        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task ManuelleRechnungSendenAsync_Fehlversand_GibtWarnungZurueck()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService { FehlerMeldung = "Postmark est désactivé" };
        var schule = new School { Id = Guid.NewGuid(), Name = "École A" };
        var periode = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id };
        var leiter = new AppUser { Id = Guid.NewGuid(), UserName = "leitung", Email = "leitung@a.org" };
        var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Schools.Add(schule);
            db.Abrechnungsperioden.Add(periode);
            db.Users.Add(leiter);
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = leiter.Id, RoleId = rolle.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = leiter.Id, SchoolId = schule.Id, CanManageStudents = true });
            await db.SaveChangesAsync();
        }

        Assert.Equal((0, 1), await Create(factory, email).ManuelleRechnungSendenAsync(periode.Id));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Single(verify.Abrechnungsperioden);
    }

    [Fact]
    public async Task ManuelleRechnungErneutSendenAsync_NurBerechtigteUndOffeneManuelleRechnungen()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService();
        var schule = new School { Id = Guid.NewGuid(), Name = "École A" };
        var fremdeSchule = new School { Id = Guid.NewGuid(), Name = "École B" };
        var periode = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id, Umsatz = 15000m };
        var automatisch = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id, AutomatischDurchKlassenabschlussErstellt = true };
        var gesammelt = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id, IstSammelabrechnungAusGeschlossenenKlassen = true };
        var bestaetigt = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id, Status = AbrechnungStatus.Bestaetigt };
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin" };
        var owner = new AppUser { Id = Guid.NewGuid(), UserName = "owner" };
        var fremderOwner = new AppUser { Id = Guid.NewGuid(), UserName = "fremd" };
        var leiter = new AppUser { Id = Guid.NewGuid(), UserName = "leitung", Email = "leitung@a.org" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Schools.AddRange(schule, fremdeSchule);
            db.Abrechnungsperioden.AddRange(periode, automatisch, gesammelt, bestaetigt);
            db.Users.AddRange(admin, owner, fremderOwner, leiter);
            var adminRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            var ownerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner };
            var leiterRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            db.Roles.AddRange(adminRolle, ownerRolle, leiterRolle);
            db.UserRoles.AddRange(
                new AppUserRole { UserId = admin.Id, RoleId = adminRolle.Id },
                new AppUserRole { UserId = owner.Id, RoleId = ownerRolle.Id },
                new AppUserRole { UserId = fremderOwner.Id, RoleId = ownerRolle.Id },
                new AppUserRole { UserId = leiter.Id, RoleId = leiterRolle.Id });
            db.UserSchools.AddRange(
                new UserSchool { AppUserId = owner.Id, SchoolId = schule.Id },
                new UserSchool { AppUserId = fremderOwner.Id, SchoolId = fremdeSchule.Id },
                new UserSchool { AppUserId = leiter.Id, SchoolId = schule.Id, CanManageStudents = true });
            await db.SaveChangesAsync();
        }

        var service = Create(factory, email);
        Assert.Equal((false, 0, 0), await service.ManuelleRechnungErneutSendenAsync(periode.Id, fremderOwner.Id));
        Assert.Equal((false, 0, 0), await service.ManuelleRechnungErneutSendenAsync(periode.Id, leiter.Id));
        Assert.Equal((false, 0, 0), await service.ManuelleRechnungErneutSendenAsync(periode.Id, Guid.NewGuid()));
        Assert.Equal((false, 0, 0), await service.ManuelleRechnungErneutSendenAsync(automatisch.Id, admin.Id));
        Assert.Equal((false, 0, 0), await service.ManuelleRechnungErneutSendenAsync(gesammelt.Id, admin.Id));
        Assert.Equal((false, 0, 0), await service.ManuelleRechnungErneutSendenAsync(bestaetigt.Id, admin.Id));
        Assert.Empty(email.Gesendet);

        Assert.Equal((true, 1, 0), await service.ManuelleRechnungErneutSendenAsync(periode.Id, owner.Id));
        Assert.Equal((true, 1, 0), await service.ManuelleRechnungErneutSendenAsync(periode.Id, admin.Id));
        Assert.Equal(2, email.Gesendet.Count);
        Assert.All(email.Gesendet, item => Assert.Equal(leiter.Email, item.Recipient.Email));
        await using var verify = await factory.CreateDbContextAsync();
        Assert.Equal(4, verify.Abrechnungsperioden.Count());
        Assert.Equal(15000m, verify.Abrechnungsperioden.Single(item => item.Id == periode.Id).Umsatz);
    }

    [Fact]
    public async Task ManuelleRechnungErneutSendenAsync_Fehlversand_RechnungBleibtUnveraendert()
    {
        var factory = new TestApplicationDbContextFactory();
        var email = new FakeEmailService { FehlerMeldung = "Postmark est désactivé" };
        var schule = new School { Id = Guid.NewGuid(), Name = "École A" };
        var periode = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = schule.Id, Umsatz = 12000m };
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin" };
        var leiter = new AppUser { Id = Guid.NewGuid(), UserName = "leitung", Email = "leitung@a.org" };
        var adminRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
        var leiterRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Schools.Add(schule);
            db.Abrechnungsperioden.Add(periode);
            db.Users.AddRange(admin, leiter);
            db.Roles.AddRange(adminRolle, leiterRolle);
            db.UserRoles.AddRange(new AppUserRole { UserId = admin.Id, RoleId = adminRolle.Id }, new AppUserRole { UserId = leiter.Id, RoleId = leiterRolle.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = leiter.Id, SchoolId = schule.Id, CanManageStudents = true });
            await db.SaveChangesAsync();
        }

        Assert.Equal((true, 0, 1), await Create(factory, email).ManuelleRechnungErneutSendenAsync(periode.Id, admin.Id));
        await using var verify = await factory.CreateDbContextAsync();
        var gespeichert = Assert.Single(verify.Abrechnungsperioden);
        Assert.Equal(AbrechnungStatus.Erstellt, gespeichert.Status);
        Assert.Equal(12000m, gespeichert.Umsatz);
    }
}
