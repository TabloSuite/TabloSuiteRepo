using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class MailboxServiceTests
{
    [Fact]
    public async Task EingehendeMail_NurZugewiesenerOwnerUndAdminSehenSie()
    {
        var factory = new TestApplicationDbContextFactory();
        var schoolId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var role = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner };
            var adminRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            var admin = new AppUser { Id = adminId, UserName = "admin" };
            admin.UserRoles.Add(new AppUserRole { UserId = adminId, RoleId = adminRole.Id, Role = adminRole, AppUser = admin });
            var owner = new AppUser { Id = ownerId, UserName = "owner", FullName = "Owner" };
            owner.UserRoles.Add(new AppUserRole { UserId = ownerId, RoleId = role.Id, Role = role, AppUser = owner });
            db.Roles.Add(role);
            db.Roles.Add(adminRole);
            db.Users.Add(admin);
            db.Users.Add(owner);
            db.UserSchools.Add(new UserSchool { AppUserId = ownerId, SchoolId = schoolId });
            db.Schools.Add(new School { Id = schoolId, Name = "École" });
            db.Users.Add(new AppUser { Id = strangerId, UserName = "stranger" });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "sender", Email = "student@example.org", UserSchools = [new UserSchool { SchoolId = schoolId }] });
            await db.SaveChangesAsync();
        }

        var service = new MailboxService(factory, new FakeEmailService());
        await service.EmpfangenAsync("student@example.org", "contact.skoulou@etsetech.com", "Sujet", "<p>Bonjour</p>", "msg-1");
        await service.EmpfangenAsync("student@example.org", "contact.skoulou@etsetech.com", "Sujet", "<p>Bonjour</p>", "msg-1");
        await service.EmpfangenAsync("unknown@example.org", "contact.skoulou@etsetech.com", "Autre", "Bonjour", "msg-2");

        Assert.Equal(2, (await service.LadeAsync(adminId, true, null)).Count);
        Assert.Empty(await service.LadeAsync(ownerId, false, null));
        Assert.Empty(await service.LadeAsync(strangerId, false, null));
        var zuzuordnendeMail = (await service.LadeAsync(adminId, true, null)).Single(mail => mail.PostmarkMessageID == "msg-1");
        Assert.True(await service.ZuordnenAsync(adminId, zuzuordnendeMail.Id, schoolId));
        Assert.Single(await service.LadeAsync(ownerId, false, null));
        Assert.Single(await service.LadeAsync(adminId, true, ownerId));
    }

    [Fact]
    public async Task ProductOwner_KannNichtOhneZugewieseneSchuleSenden()
    {
        var fake = new FakeEmailService();
        var service = new MailboxService(new TestApplicationDbContextFactory(), fake);
        var result = await service.SendenAsync(Guid.NewGuid(), false, Guid.NewGuid(), "test@example.org", "Sujet", "Bonjour");
        Assert.False(result.Succeeded);
        Assert.Empty(fake.Gesendet);
    }

    [Fact]
    public async Task ProductOwner_MussSchuleWaehlenUndDarfNurEigeneSchuleAnschreiben()
    {
        var factory = new TestApplicationDbContextFactory();
        var ownerId = Guid.NewGuid();
        var schoolId = Guid.NewGuid();
        var otherSchoolId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var role = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner };
            db.Roles.Add(role);
            db.Schools.AddRange(new School { Id = schoolId, Name = "École A" }, new School { Id = otherSchoolId, Name = "École B" });
            db.Users.Add(new AppUser { Id = ownerId, UserName = "owner", UserRoles = [new AppUserRole { RoleId = role.Id }],
                UserSchools = [new UserSchool { SchoolId = schoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "student", Email = "student@example.org",
                UserSchools = [new UserSchool { SchoolId = schoolId }, new UserSchool { SchoolId = otherSchoolId }] });
            await db.SaveChangesAsync();
        }

        var fake = new FakeEmailService();
        var service = new MailboxService(factory, fake);
        var withoutSchool = await service.SendenAsync(ownerId, false, null, "student@example.org", "Sujet", "<p>Bonjour</p>");
        Assert.False(withoutSchool.Succeeded);
        Assert.Empty(fake.Gesendet);

        var sent = await service.SendenAsync(ownerId, false, schoolId, "student@example.org", "Sujet", "<p>Bonjour</p>");
        Assert.True(sent.Succeeded);
        Assert.Single(fake.Gesendet);
        Assert.Equal(schoolId, (await service.LadeAsync(ownerId, false, null)).Single().SchulID);

        var denied = await service.SendenAsync(ownerId, false, schoolId, "external@example.org", "Sujet", "Bonjour");
        Assert.False(denied.Succeeded);
        Assert.Single(fake.Gesendet);
    }

    [Fact]
    public async Task SuperAdmin_OhneSchulauswahl_KannIndividuelleMailSenden()
    {
        var factory = new TestApplicationDbContextFactory();
        var adminId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var role = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            db.Roles.Add(role);
            db.Users.Add(new AppUser { Id = adminId, UserName = "admin", UserRoles = [new AppUserRole { RoleId = role.Id }] });
            await db.SaveChangesAsync();
        }

        var fake = new FakeEmailService();
        var service = new MailboxService(factory, fake);
        var result = await service.SendenAsync(adminId, true, null, "external@example.org", "Sujet", "<p>Bonjour</p>");

        Assert.True(result.Succeeded);
        Assert.Single(fake.Gesendet);
        Assert.Null((await service.LadeAsync(adminId, true, null)).Single().SchulID);
    }

    [Fact]
    public async Task EmpfaengerAsync_BeschraenktKontakteAufSchuleUndProductOwnerZugriff()
    {
        var factory = new TestApplicationDbContextFactory();
        var adminId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var schoolId = Guid.NewGuid();
        var otherSchoolId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var adminRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            var ownerRole = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner };
            var directorRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            var coordinatorRole = new Role { Id = Guid.NewGuid(), Name = Rollen.Koordinator };
            var teacherRole = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer };
            var studentRole = new Role { Id = Guid.NewGuid(), Name = Rollen.Schueler };
            db.Roles.AddRange(adminRole, ownerRole, directorRole, coordinatorRole, teacherRole, studentRole);
            db.Schools.AddRange(new School { Id = schoolId, Name = "École A" }, new School { Id = otherSchoolId, Name = "École B" });
            db.Users.Add(new AppUser { Id = adminId, UserName = "admin", UserRoles = [new AppUserRole { RoleId = adminRole.Id }] });
            db.Users.Add(new AppUser { Id = ownerId, UserName = "owner", UserRoles = [new AppUserRole { RoleId = ownerRole.Id }],
                UserSchools = [new UserSchool { SchoolId = schoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "director", FullName = "Directrice", Email = "director@example.org",
                UserRoles = [new AppUserRole { RoleId = directorRole.Id }], UserSchools = [new UserSchool { SchoolId = schoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "coordinator", FullName = "Coordinateur", Email = "coordinator@example.org",
                UserRoles = [new AppUserRole { RoleId = coordinatorRole.Id }], UserSchools = [new UserSchool { SchoolId = schoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "teacher", FullName = "Enseignant", Email = "teacher@example.org",
                UserRoles = [new AppUserRole { RoleId = teacherRole.Id }], UserSchools = [new UserSchool { SchoolId = schoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "student", FullName = "Élève", Email = "student@example.org",
                UserRoles = [new AppUserRole { RoleId = studentRole.Id }], UserSchools = [new UserSchool { SchoolId = schoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "other", Email = "other@example.org",
                UserRoles = [new AppUserRole { RoleId = studentRole.Id }], UserSchools = [new UserSchool { SchoolId = otherSchoolId }] });
            db.Users.Add(new AppUser { Id = Guid.NewGuid(), UserName = "blocked", Email = "blocked@example.org", IsBlocked = true,
                UserRoles = [new AppUserRole { RoleId = teacherRole.Id }], UserSchools = [new UserSchool { SchoolId = schoolId }] });
            await db.SaveChangesAsync();
        }

        var service = new MailboxService(factory, new FakeEmailService());
        var contacts = await service.EmpfaengerAsync(ownerId, schoolId);
        Assert.Equal(4, contacts.Count);
        Assert.True(contacts.Single(contact => contact.Email == "director@example.org").IstDirektion);
        Assert.True(contacts.Single(contact => contact.Email == "coordinator@example.org").IstDirektion);
        Assert.True(contacts.Single(contact => contact.Email == "teacher@example.org").IstLehrer);
        Assert.True(contacts.Single(contact => contact.Email == "student@example.org").IstSchueler);
        Assert.Empty(await service.EmpfaengerAsync(ownerId, otherSchoolId));
        Assert.Empty(await service.EmpfaengerAsync(Guid.NewGuid(), schoolId));
        Assert.Single(await service.EmpfaengerAsync(adminId, otherSchoolId));
    }

    [Fact]
    public async Task SendenAnGruppeAsync_NurAdminDarfSendenUndErfolgreicheMailsSindImPostausgang()
    {
        var factory = new TestApplicationDbContextFactory();
        var adminId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var adminRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            var ownerRole = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner };
            db.Roles.AddRange(adminRole, ownerRole);
            db.Users.AddRange(new AppUser { Id = adminId, UserName = "admin" }, new AppUser { Id = ownerId, UserName = "owner" });
            db.UserRoles.AddRange(new AppUserRole { UserId = adminId, RoleId = adminRole.Id },
                new AppUserRole { UserId = ownerId, RoleId = ownerRole.Id });
            await db.SaveChangesAsync();
        }
        var fake = new FakeEmailService();
        var service = new MailboxService(factory, fake);
        var criteria = new BroadcastCriteria
        {
            Audience = BroadcastAudience.ManualAddresses,
            ZusaetzlicheAdressen = ["a@example.org", "b@example.org"]
        };
        var mail = new EmailMessage { Subject = "Nouvelles", HtmlBody = "<p>Bonjour</p>" };

        var denied = await service.SendenAnGruppeAsync(ownerId, criteria, mail);
        Assert.Equal(0, denied.SentCount);
        Assert.Empty(fake.Gesendet);

        var sent = await service.SendenAnGruppeAsync(adminId, criteria, mail);
        Assert.Equal(2, sent.SentCount);
        Assert.Equal(2, (await service.LadeAsync(adminId, true, null)).Count);

        fake.FehlerMeldung = "Échec d'envoi";
        var failed = await service.SendenAnGruppeAsync(adminId, criteria, mail);
        Assert.Equal(0, failed.SentCount);
        Assert.Equal(2, failed.FailedCount);
        Assert.Equal(2, (await service.LadeAsync(adminId, true, null)).Count);
    }
}
