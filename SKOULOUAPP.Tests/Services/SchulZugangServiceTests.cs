using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SchulZugangServiceTests
{
    [Fact]
    public async Task DirektorsperreWirktNurInZugeordneterSchuleUndVeraendertKeineNutzerflags()
    {
        using var db = TestDbContext.CreateInMemory();
        var a = new School { Id = Guid.NewGuid(), Name = "A" }; var b = new School { Id = Guid.NewGuid(), Name = "B" };
        var direktor = new AppUser { Id = Guid.NewGuid(), IsBlocked = true };
        var lehrer = new AppUser { Id = Guid.NewGuid() }; var student = new AppUser { Id = Guid.NewGuid() };
        var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        db.Schools.AddRange(a, b); db.Users.AddRange(direktor, lehrer, student); db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { RoleId = rolle.Id, UserId = direktor.Id });
        db.UserSchools.AddRange(new UserSchool { AppUserId = direktor.Id, SchoolId = a.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = a.Id }, new UserSchool { AppUserId = lehrer.Id, SchoolId = b.Id });
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = a.Id });
        await db.SaveChangesAsync();
        var service = new SchulZugangService(db);
        Assert.False(await service.IstSchuleFreigegebenAsync(a.Id)); Assert.True(await service.IstSchuleFreigegebenAsync(b.Id));
        Assert.False(await service.DarfAnmeldenAsync(student.Id)); Assert.True(await service.DarfAnmeldenAsync(lehrer.Id));
        Assert.False(lehrer.IsBlocked); Assert.False(student.IsBlocked);
        direktor.IsBlocked = false; await db.SaveChangesAsync();
        Assert.True(await service.DarfAnmeldenAsync(student.Id));
        student.IsBlocked = true; await db.SaveChangesAsync();
        Assert.False(await service.DarfAnmeldenAsync(student.Id));
    }
}
