using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Tests.Infrastructure;
using HalloApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HalloApp.Tests.Services;

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
}
