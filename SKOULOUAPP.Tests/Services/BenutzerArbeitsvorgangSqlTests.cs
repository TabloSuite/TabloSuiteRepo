using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.Import;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Storage;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public sealed class BenutzerArbeitsvorgangSqlTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task BenutzeranlageRolltIdentityProfilUndSchulzuordnungGemeinsamZurueck()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        var name = "SKOULOUAPP_TEST_" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        var commitAntwort = new CommitAntwortVerlustInterceptor();
        services.AddLogging();
        services.AddDbContextFactory<ApplicationDbContext>(o => o.UseSqlServer($"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Encrypt=True", s => s.EnableRetryOnFailure()).AddInterceptors(commitAntwort));
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddTransient<IUserValidator<AppUser>, ImportFehlerValidator>();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddSingleton<IMediaStorageService, FakeMediaStorageService>();
        services.AddTransient<IBenutzerArbeitsvorgang, BenutzerArbeitsvorgang>();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        try
        {
            await db.Database.MigrateAsync();
            var school = new School { Id = Guid.NewGuid(), Name = "Atomic SQL" };
            db.Schools.Add(school);
            db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = Rollen.Schueler, NormalizedName = Rollen.Schueler.ToUpperInvariant() });
            await db.SaveChangesAsync();
            UserFormModel Form(string email) => new() { Email = email, Vorname = "Afi", Nachname = "Test", RoleName = Rollen.Schueler,
                Password = "Individuel7!", SchoolIds = [school.Id] };
            var operation = provider.GetRequiredService<IBenutzerArbeitsvorgang>();
            var rollback = await operation.AusfuehrenAsync(async (dashboard, _) =>
            {
                var saved = await dashboard.SaveUserAsync(Form("rollback@sql.test"));
                Assert.True(saved.Succeeded, saved.Message);
                return false; // Fehler eines späteren Schritts derselben Operation.
            }, result => result, default);
            Assert.False(rollback);
            Assert.Empty(await db.Users.ToListAsync()); Assert.Empty(await db.StudentProfiles.ToListAsync());
            Assert.Empty(await db.UserSchools.ToListAsync());
            var committed = await operation.AusfuehrenAsync((dashboard, _) => dashboard.SaveUserAsync(Form("commit@sql.test")), result => result.Succeeded, default);
            Assert.True(committed.Succeeded, committed.Message);
            var user = Assert.Single(await db.Users.ToListAsync()); Assert.NotNull(user.PasswordHash);
            Assert.Single(await db.StudentProfiles.ToListAsync()); Assert.Single(await db.UserSchools.ToListAsync());

            // Fehler erst in Zeile 2 nach erfolgreicher Identity-Anlage von Zeile 1:
            // Vorschau allein kann diesen realen Identity-Fehler nicht abfangen.
            var director = new AppUser { Id = Guid.NewGuid(), UserName = "director", Email = "director@sql.test" };
            var role = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter, NormalizedName = Rollen.SchulLeiter.ToUpperInvariant() };
            var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CM1" };
            school.IsActive = true;
            db.Users.Add(director); db.Roles.Add(role); db.Klassen.Add(klasse);
            db.UserRoles.Add(new AppUserRole { UserId = director.Id, RoleId = role.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
            await db.SaveChangesAsync();
            var import = new SchoolUserImportService(db,
                new DashboardService(db, TestUserManager.Create(), new FakeMediaStorageService()), operation);
            var request = new SchoolUserImportCommitRequest
            {
                DirectorId = director.Id, SchoolId = school.Id, Kind = SchoolUserImportKind.Student,
                CsvContent = "Vorname;Nachname;Klasse;Email\nAfi;Un;CM1;first@sql.test\nKomi;Deux;CM1;refuse@sql.test"
            };
            var failed = await import.CommitAsync(request);
            Assert.False(failed.Succeeded);
            Assert.Equal(0, failed.CreatedCount); Assert.Empty(failed.Zugangsdaten);
            Assert.False(await db.Users.AnyAsync(x => x.Email == "first@sql.test" || x.Email == "refuse@sql.test"));
            Assert.Single(await db.StudentProfiles.ToListAsync());
            Assert.Equal(2, await db.UserSchools.CountAsync());

            request.CsvContent = request.CsvContent.Replace("refuse@sql.test", "second@sql.test");
            commitAntwort.NaechsteAntwortVerlieren = true;
            var success = await import.CommitAsync(request);
            Assert.True(success.Succeeded, success.Message);
            Assert.Equal(2, success.CreatedCount); Assert.Equal(2, success.Zugangsdaten.Count);
            Assert.NotEqual(success.Zugangsdaten[0].Passwort, success.Zugangsdaten[1].Passwort);
            Assert.Equal(3, await db.StudentProfiles.CountAsync());
            Assert.Equal(1, commitAntwort.VerloreneAntworten);
            Assert.Equal(2, await db.BenutzerVorgangsBestaetigungen.CountAsync());
        }
        finally
        {
            if (!name.StartsWith("SKOULOUAPP_TEST_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe database name.");
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class ImportFehlerValidator : IUserValidator<AppUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
            => Task.FromResult(user.Email == "refuse@sql.test"
                ? IdentityResult.Failed(new IdentityError { Code = "TestFailure", Description = "Échec contrôlé de la deuxième ligne." })
                : IdentityResult.Success);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
