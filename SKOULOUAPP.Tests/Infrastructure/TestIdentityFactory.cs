using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Tests.Infrastructure;

public static class TestIdentityFactory
{
    public static async Task<Guid> AddSchoolLeaderAsync(TestDbContext db, Guid schoolId)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = Rollen.SchulLeiter,
            NormalizedName = "SCHULLEITER"
        };
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Directeur",
            UserName = "directeur"
        };

        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = role.Id });
        db.UserSchools.Add(new UserSchool { AppUserId = user.Id, SchoolId = schoolId, CanManageStudents = true });
        await db.SaveChangesAsync();
        return user.Id;
    }

    public static async Task<Guid> AddSuperAdminAsync(TestDbContext db)
    {
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Name = Rollen.SuperAdmin,
            NormalizedName = "SUPERADMIN"
        };
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Administrateur",
            UserName = "administrateur"
        };

        db.Roles.Add(role);
        db.Users.Add(user);
        db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        return user.Id;
    }
}