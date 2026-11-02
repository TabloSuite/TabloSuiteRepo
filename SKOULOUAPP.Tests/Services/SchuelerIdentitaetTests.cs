using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Options;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public sealed class SchuelerIdentitaetTests
{
    [Fact]
    public async Task EmailUndPasswortwechsel_BewahrenIdentitaetMatrikelUndLerndaten()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var datenbank = Guid.NewGuid().ToString();
        services.AddDbContextFactory<ApplicationDbContext>(options => options.UseInMemoryDatabase(datenbank));
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var factory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var schule = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM1A", Title = "CM1 A" };
        var schueler = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "ancien@test.local", Email = "ancien@test.local",
            EmailConfirmed = true, Vorname = "Afi", Nachname = "Kossi", ActiveSessionId = Guid.NewGuid()
        };
        db.Schools.Add(schule);
        db.Klassen.Add(klasse);
        db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = Rollen.Schueler, NormalizedName = Rollen.Schueler.ToUpperInvariant() });
        await db.SaveChangesAsync();
        Assert.True((await manager.CreateAsync(schueler, "AncienSecret7!")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(schueler, Rollen.Schueler)).Succeeded);
        var profil = new StudentProfile
        {
            Id = Guid.NewGuid(), AppUserId = schueler.Id, SchoolId = schule.Id,
            CurrentKlasseId = klasse.Id, CurrentLevelCode = klasse.Code, Matrikelnummer = "EL-2026-12045"
        };
        var lektion = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Lecture", Order = 1 };
        var fortschritt = new LessonProgress { Id = Guid.NewGuid(), StudentId = schueler.Id, LessonId = lektion.Id, IsCompleted = true };
        var teilnahme = new LevelEnrollment { Id = Guid.NewGuid(), StudentId = schueler.Id, KlasseId = klasse.Id, IsUnlocked = true };
        db.StudentProfiles.Add(profil);
        db.UserSchools.Add(new UserSchool { AppUserId = schueler.Id, SchoolId = schule.Id });
        db.Lessons.Add(lektion);
        db.LessonProgresses.Add(fortschritt);
        db.LevelEnrollments.Add(teilnahme);
        await db.SaveChangesAsync();
        var alterHash = schueler.PasswordHash;
        var dashboard = new DashboardService(db, manager, new FakeMediaStorageService());

        var emailwechsel = await dashboard.SaveUserAsync(new UserFormModel
        {
            UserId = schueler.Id, Vorname = schueler.Vorname, Nachname = schueler.Nachname,
            Email = "Nouveau@Test.local", RoleName = Rollen.Schueler,
            SchoolIds = [schule.Id], CurrentKlasseId = klasse.Id
        });

        Assert.True(emailwechsel.Succeeded, emailwechsel.Message);
        Assert.Equal("Nouveau@Test.local", schueler.Email);
        Assert.Equal(schueler.Email, schueler.UserName);
        Assert.Equal("NOUVEAU@TEST.LOCAL", schueler.NormalizedEmail);
        Assert.Equal(schueler.NormalizedEmail, schueler.NormalizedUserName);
        Assert.Equal(alterHash, schueler.PasswordHash);
        Assert.Equal(schueler.Id, (await manager.FindByEmailAsync("nouveau@test.local"))!.Id);
        Assert.Null(await manager.FindByEmailAsync("ancien@test.local"));
        var auth = new AuthService(db, factory, manager, null!, new FakeEmailService(),
            Options.Create(new PostmarkOptions()), new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);
        var alteSitzung = schueler.ActiveSessionId;
        var falschesPasswort = await auth.ChangePasswordAsync(schueler.Id, "Falsch7!", "NouveauSecret8!");
        Assert.False(falschesPasswort.Succeeded);
        Assert.Equal(alteSitzung, schueler.ActiveSessionId);
        Assert.Equal(alterHash, schueler.PasswordHash);

        var passwortwechsel = await auth.ChangePasswordAsync(schueler.Id, "AncienSecret7!", "NouveauSecret8!");

        Assert.True(passwortwechsel.Succeeded, passwortwechsel.Message);
        Assert.NotEqual(alteSitzung, schueler.ActiveSessionId);
        Assert.False(await manager.CheckPasswordAsync(schueler, "AncienSecret7!"));
        Assert.True(await manager.CheckPasswordAsync(schueler, "NouveauSecret8!"));
        await using var pruefung = await factory.CreateDbContextAsync();
        var gespeicherterSchueler = Assert.Single(await pruefung.Users.ToListAsync());
        Assert.Equal(schueler.Id, gespeicherterSchueler.Id);
        var gespeichertesProfil = Assert.Single(await pruefung.StudentProfiles.ToListAsync());
        Assert.Equal(profil.Id, gespeichertesProfil.Id);
        Assert.Equal(schueler.Id, gespeichertesProfil.AppUserId);
        Assert.Equal("EL-2026-12045", gespeichertesProfil.Matrikelnummer);
        Assert.Equal(klasse.Id, gespeichertesProfil.CurrentKlasseId);
        var gespeicherterFortschritt = Assert.Single(await pruefung.LessonProgresses.ToListAsync());
        Assert.Equal(fortschritt.Id, gespeicherterFortschritt.Id);
        Assert.Equal(schueler.Id, gespeicherterFortschritt.StudentId);
        Assert.True(gespeicherterFortschritt.IsCompleted);
        Assert.Equal(teilnahme.Id, Assert.Single(await pruefung.LevelEnrollments.ToListAsync()).Id);
        Assert.Equal(schueler.Id, Assert.Single(await pruefung.UserSchools.ToListAsync()).AppUserId);
    }
}
