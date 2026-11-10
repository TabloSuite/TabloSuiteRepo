using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für <see cref="DashboardService"/> auf Basis eines In-Memory-Kontexts
/// und eines Test-UserManagers.
/// </summary>
public class DashboardServiceTests
{
    [Fact]
    public async Task GetStudentDashboardAsync_BewahrtAbschlussnotenAusWiederholtenCyclesOhneKlassenZuordnung()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1", Title = "CP1" };
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "student", FullName = "Élève" };
        var profil = new StudentProfile { Id = Guid.NewGuid(), SchoolId = school.Id, AppUserId = student.Id };
        var erster = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = klasse.Id, StartDatum = DateTime.UtcNow.AddMonths(-8), EndDatum = DateTime.UtcNow.AddMonths(-5) };
        var zweiter = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = klasse.Id, StartDatum = DateTime.UtcNow.AddMonths(-4), EndDatum = DateTime.UtcNow.AddDays(-1) };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Users.Add(student);
        db.StudentProfiles.Add(profil);
        db.KursNiveauDurchlaeufe.AddRange(erster, zweiter);
        db.KlassenAbschlussEntscheidungen.AddRange(
            new KlassenAbschlussEntscheidung { DurchlaufId = erster.Id, StudentId = student.Id, AbschlussNoteText = "8/20", MentionText = "À renforcer", IsBestanden = false },
            new KlassenAbschlussEntscheidung { DurchlaufId = zweiter.Id, StudentId = student.Id, AbschlussNoteText = "14/20", MentionText = "Bien", IsBestanden = true });
        await db.SaveChangesAsync();
        var service = new DashboardService(db, TestUserManager.Create([student]), new FakeMediaStorageService());

        var dashboard = await service.GetStudentDashboardAsync(student.Id);

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.ClassHistory.Count);
        Assert.All(dashboard.ClassHistory, item => Assert.False(item.IsCurrent));
        Assert.Equal("14/20", dashboard.ClassHistory[0].AbschlussNoteText);
        Assert.Equal("Bien", dashboard.ClassHistory[0].MentionText);
        Assert.True(dashboard.ClassHistory[0].IsBestanden);
        Assert.Equal("8/20", dashboard.ClassHistory[1].AbschlussNoteText);
        Assert.Equal("À renforcer", dashboard.ClassHistory[1].MentionText);
        Assert.False(dashboard.ClassHistory[1].IsBestanden);
    }

    [Fact]
    public async Task CreatePlatformUserAsync_ProductOwnerKannNurDirektorSeinerSchuleAnlegen()
    {
        using var db = TestDbContext.CreateInMemory();
        var owner = new AppUser { Id = Guid.NewGuid(), UserName = "owner", Email = "owner@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        var other = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.Add(owner);
        db.Schools.AddRange(school, other);
        db.UserSchools.Add(new UserSchool { AppUserId = owner.Id, SchoolId = school.Id });
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create([owner]);
        manager.SeedRole(owner, Rollen.ProduktOwner);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        var forbidden = await service.CreatePlatformUserAsync(owner.Id, new UserFormModel
        { Vorname = "Un", Nachname = "Prof", Email = "prof@test.local", RoleName = Rollen.Lehrer, SchoolIds = [school.Id] });
        var foreign = await service.CreatePlatformUserAsync(owner.Id, new UserFormModel
        { Vorname = "Un", Nachname = "Directeur", Email = "foreign@test.local", RoleName = Rollen.SchulLeiter, SchoolIds = [other.Id] });
        var allowed = await service.CreatePlatformUserAsync(owner.Id, new UserFormModel
        { Vorname = "Un", Nachname = "Directeur", Email = "director@test.local", RoleName = Rollen.SchulLeiter, SchoolIds = [school.Id], Password = "Start123!" });

        Assert.False(forbidden.Succeeded);
        Assert.False(foreign.Succeeded);
        Assert.True(allowed.Succeeded, allowed.Message);
        Assert.Single(await service.GetPlatformSchoolsAsync(owner.Id));
        Assert.DoesNotContain(await service.GetPlatformUsersAsync(owner.Id), user => user.RoleName == Rollen.ProduktOwner);
    }

    [Fact]
    public async Task CreatePlatformUserAsync_AdminOrdnetOwnerSchulenOhneDirektorrechtZu()
    {
        using var db = TestDbContext.CreateInMemory();
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin", Email = "admin@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        db.Users.Add(admin);
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create([admin]);
        manager.SeedRole(admin, Rollen.SuperAdmin);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        var result = await service.CreatePlatformUserAsync(admin.Id, new UserFormModel
        { Vorname = "Une", Nachname = "Responsable", Email = "owner.new@test.local", RoleName = Rollen.ProduktOwner,
            SchoolIds = [school.Id], Password = "Start123!" });

        Assert.True(result.Succeeded, result.Message);
        var link = Assert.Single(db.UserSchools.Where(link => link.SchoolId == school.Id));
        Assert.False(link.CanManageStudents);
        Assert.Single(await service.GetPlatformSchoolsAsync(link.AppUserId));
    }

    [Fact]
    public async Task GetPlatformUsersAsync_OwnerSiehtBeiGeteiltemNutzerNurEigeneSchulnamen()
    {
        using var db = TestDbContext.CreateInMemory();
        var owner = new AppUser { Id = Guid.NewGuid(), UserName = "owner" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "teacher" };
        var own = new School { Id = Guid.NewGuid(), Name = "École A" };
        var foreign = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.AddRange(owner, teacher);
        db.Schools.AddRange(own, foreign);
        db.UserSchools.AddRange(new UserSchool { AppUserId = owner.Id, SchoolId = own.Id },
            new UserSchool { AppUserId = teacher.Id, SchoolId = own.Id },
            new UserSchool { AppUserId = teacher.Id, SchoolId = foreign.Id });
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create([owner, teacher]);
        manager.SeedRole(owner, Rollen.ProduktOwner);
        manager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        Assert.Equal([own.Name], Assert.Single(await service.GetPlatformUsersAsync(owner.Id)).Schools);
    }

    [Fact]
    public async Task SetPlatformUserBlockedAsync_ProductOwnerKannNurEigeneSchulnutzerBlockieren()
    {
        using var db = TestDbContext.CreateInMemory();
        var owner = new AppUser { Id = Guid.NewGuid(), UserName = "owner", Email = "owner@test.local" };
        var own = new AppUser { Id = Guid.NewGuid(), UserName = "own", Email = "own@test.local" };
        var foreign = new AppUser { Id = Guid.NewGuid(), UserName = "foreign", Email = "foreign@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        var other = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.AddRange(owner, own, foreign);
        db.Schools.AddRange(school, other);
        db.UserSchools.AddRange(new UserSchool { AppUserId = owner.Id, SchoolId = school.Id },
            new UserSchool { AppUserId = own.Id, SchoolId = school.Id },
            new UserSchool { AppUserId = foreign.Id, SchoolId = other.Id });
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create([owner, own, foreign]);
        manager.SeedRole(owner, Rollen.ProduktOwner);
        manager.SeedRole(own, Rollen.Lehrer);
        manager.SeedRole(foreign, Rollen.Lehrer);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        Assert.False((await service.SetPlatformUserBlockedAsync(owner.Id, foreign.Id, true)).Succeeded);
        Assert.True((await service.SetPlatformUserBlockedAsync(owner.Id, own.Id, true)).Succeeded);
        Assert.True(own.IsBlocked);
        Assert.False(foreign.IsBlocked);
        Assert.DoesNotContain(await service.GetPlatformUsersAsync(owner.Id), user => user.UserId == foreign.Id);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_KoordinatorKannKeinenWeiterenKoordinatorAnlegen()
    {
        using var db = TestDbContext.CreateInMemory();
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "coordinateur", Email = "coordinateur@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        db.Users.Add(coordinator);
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = coordinator.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([coordinator]);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolUserAsync(coordinator.Id, new UserFormModel
        {
            Vorname = "Autre",
            Nachname = "Coordinateur",
            Email = "autre.coordinateur@test.local",
            RoleName = Rollen.Koordinator,
            Password = "Start123!",
            SchoolIds = [school.Id]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("pas autorisé", result.Message);
    }

    [Fact]
    public async Task SaveUserAsync_SchulLeiterErhaeltGenauEineSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var firstSchool = new School { Id = Guid.NewGuid(), Name = "École A", City = "Lomé" };
        var secondSchool = new School { Id = Guid.NewGuid(), Name = "École B", City = "Kara" };
        db.Schools.AddRange(firstSchool, secondSchool);
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([]);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());
        UserFormModel Formular(params Guid[] schulen) => new()
        {
            Vorname = "Marie",
            Nachname = "Directrice",
            Email = "marie.directrice@test.local",
            RoleName = "SchulLeiter",
            Password = "Start123!",
            SchoolIds = [.. schulen]
        };

        // Zwei Schulen für ein Direktionskonto werden abgelehnt.
        var zweiSchulen = await service.SaveUserAsync(Formular(firstSchool.Id, secondSchool.Id));
        Assert.False(zweiSchulen.Succeeded);
        Assert.Equal(SchulZuordnungsRegel.MeldungGenauEineSchule, zweiSchulen.Message);
        Assert.Empty(await db.UserSchools.ToListAsync());

        var result = await service.SaveUserAsync(Formular(firstSchool.Id));
        Assert.True(result.Succeeded, result.Message);
        var link = Assert.Single(await db.UserSchools.ToListAsync());
        Assert.Equal(firstSchool.Id, link.SchoolId);
        Assert.False(link.IstPlattformZuordnung);

        // Die Schule eines bestehenden Kontos ist fest: kein Wechsel in eine andere Schule.
        var wechsel = Formular(secondSchool.Id);
        wechsel.UserId = link.AppUserId;
        var wechselErgebnis = await service.SaveUserAsync(wechsel);
        Assert.False(wechselErgebnis.Succeeded);
        Assert.Equal(SchulZuordnungsRegel.MeldungSchuleFest, wechselErgebnis.Message);
        Assert.Equal(firstSchool.Id, Assert.Single(await db.UserSchools.ToListAsync()).SchoolId);
    }

    [Fact]
    public async Task SaveUserAsync_ProduktOwnerBetreutMehrereSchulenAlsPlattformzuordnung()
    {
        using var db = TestDbContext.CreateInMemory();
        var firstSchool = new School { Id = Guid.NewGuid(), Name = "École A", City = "Lomé" };
        var secondSchool = new School { Id = Guid.NewGuid(), Name = "École B", City = "Kara" };
        db.Schools.AddRange(firstSchool, secondSchool);
        await db.SaveChangesAsync();
        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());

        var result = await service.SaveUserAsync(new UserFormModel
        {
            Vorname = "Paul",
            Nachname = "Produit",
            Email = "paul.produit@test.local",
            RoleName = "ProduktOwner",
            Password = "Start123!",
            SchoolIds = [firstSchool.Id, secondSchool.Id]
        });

        Assert.True(result.Succeeded, result.Message);
        var links = await db.UserSchools.ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.True(link.IstPlattformZuordnung));
    }

    [Fact]
    public async Task SetLehrerNiveauZuordnungenAsync_SpeichertKlassenDerZugewiesenenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer", Email = "lehrer@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", Title = "Sixième" };
        db.Users.AddRange(director, teacher);
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = school.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, "SchulLeiter");
        userManager.SeedRole(teacher, "Lehrer");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        await service.SetLehrerNiveauZuordnungenAsync(
            director.Id,
            teacher.Id,
            [(school.Id, klasse.Id, 3m)]);

        var assignment = await db.LehrerNiveauZuordnungen.SingleAsync();
        Assert.Equal(teacher.Id, assignment.LehrerID);
        Assert.Equal(school.Id, assignment.SchulID);
        Assert.Equal(klasse.Id, assignment.KlasseId);
        Assert.Equal(3m, assignment.Wochenstunden);
    }

    [Fact]
    public async Task SetLehrerFachZuordnungenAsync_SpeichertNurFaecherDerGemeinsamenSchulen()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter-fach", Email = "leiter.fach@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer-fach", Email = "lehrer.fach@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A", City = "Lomé" };
        var otherSchool = new School { Id = Guid.NewGuid(), Name = "École B", City = "Kara" };
        var facSkoulouwed = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Mathématiques" };
        var fachDenied = new Fach { Id = Guid.NewGuid(), SchoolId = otherSchool.Id, Name = "Histoire" };

        db.Users.AddRange(director, teacher);
        db.Schools.AddRange(school, otherSchool);
        db.Faecher.AddRange(facSkoulouwed, fachDenied);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = school.Id },
            new UserSchool { AppUserId = teacher.Id, SchoolId = otherSchool.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        await service.SetLehrerFachZuordnungenAsync(director.Id, teacher.Id, [facSkoulouwed.Id, fachDenied.Id]);

        var assignments = await db.LehrerFachZuordnungen
            .AsNoTracking()
            .Where(item => item.LehrerId == teacher.Id)
            .Select(item => item.FachId)
            .ToListAsync();

        Assert.Single(assignments);
        Assert.Contains(facSkoulouwed.Id, assignments);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_LehrerSpeichertNurFaecherDerEigenenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "director-fach-save", Email = "director.fach.save@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "teacher-fach-save", Email = "teacher.fach.save@test.local", Vorname = "Theo", Nachname = "Prof", FullName = "Theo Prof" };
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var otherSchool = new School { Id = Guid.NewGuid(), Name = "École externe", City = "Kara" };
        var facSkoulouwed = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Physique" };
        var fachDenied = new Fach { Id = Guid.NewGuid(), SchoolId = otherSchool.Id, Name = "Géographie" };

        db.Users.AddRange(director, teacher);
        db.Schools.AddRange(school, otherSchool);
        db.Faecher.AddRange(facSkoulouwed, fachDenied);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = school.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            UserId = teacher.Id,
            Vorname = "Theo",
            Nachname = "Prof",
            Email = teacher.Email!,
            RoleName = Rollen.Lehrer,
            SchoolIds = [school.Id],
            FachIds = [facSkoulouwed.Id, fachDenied.Id],
            TeacherClassAssignments = []
        });

        Assert.True(result.Succeeded, result.Message);

        var assignedFachIds = await db.LehrerFachZuordnungen
            .AsNoTracking()
            .Where(item => item.LehrerId == teacher.Id)
            .Select(item => item.FachId)
            .ToListAsync();

        Assert.Single(assignedFachIds);
        Assert.Contains(facSkoulouwed.Id, assignedFachIds);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_AltbestandMitZweiSchulenWirdNichtStillGespeichert()
    {
        // Ein Konto = eine Schule: Ein (historisches) Lehrerkonto mit einer zweiten Schule darf die
        // Direktion nicht unbemerkt weiterführen; es bleibt unverändert, bis der Altbestand bereinigt ist.
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "director", Email = "director@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "teacher", Email = "teacher@test.local", Vorname = "Theo", Nachname = "Prof" };
        var ownSchool = new School { Id = Guid.NewGuid(), Name = "École A" };
        var foreignSchool = new School { Id = Guid.NewGuid(), Name = "École C" };
        db.Users.AddRange(director, teacher);
        db.Schools.AddRange(ownSchool, foreignSchool);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = ownSchool.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = ownSchool.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = foreignSchool.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());
        var form = await service.GetSchoolUserForEditAsync(director.Id, teacher.Id);
        Assert.NotNull(form);
        Assert.Equal([ownSchool.Id], form.SchoolIds);

        form.Vorname = "Théo";
        var result = await service.SaveSchoolUserAsync(director.Id, form);

        Assert.False(result.Succeeded);
        Assert.Equal(SchulZuordnungsRegel.MeldungSchuleFest, result.Message);
        Assert.Equal(2, await db.UserSchools.CountAsync(link => link.AppUserId == teacher.Id));
    }

    [Fact]
    public async Task SaveSchoolUserAsync_NeuesKontoLandetImmerInDerSchuleDerDirektion()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "director-neu", Email = "director.neu@test.local" };
        var ownSchool = new School { Id = Guid.NewGuid(), Name = "École A" };
        var foreignSchool = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.Add(director);
        db.Schools.AddRange(ownSchool, foreignSchool);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = ownSchool.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());
        UserFormModel Lehrer(string email, params Guid[] schulen) => new()
        {
            Vorname = "Ama",
            Nachname = "Prof",
            Email = email,
            RoleName = Rollen.Lehrer,
            Password = "Start123!",
            SchoolIds = [.. schulen],
            TeacherClassAssignments = []
        };

        // Fremde oder zusätzliche Schulen werden abgelehnt.
        Assert.False((await service.SaveSchoolUserAsync(director.Id, Lehrer("ama.fremd@test.local", foreignSchool.Id))).Succeeded);
        Assert.False((await service.SaveSchoolUserAsync(director.Id, Lehrer("ama.zwei@test.local", ownSchool.Id, foreignSchool.Id))).Succeeded);

        // Ohne Angabe wird die (einzige) Schule der Direktion verwendet.
        var result = await service.SaveSchoolUserAsync(director.Id, Lehrer("ama.prof@test.local"));
        Assert.True(result.Succeeded, result.Message);
        var lehrerID = (await userManager.FindByEmailAsync("ama.prof@test.local"))!.Id;
        Assert.Equal(ownSchool.Id, (await db.UserSchools.SingleAsync(link => link.AppUserId == lehrerID)).SchoolId);
    }

    [Fact]
    public async Task GetSchoolUserForEditAsync_LehrerLaedtFachIdsDerDirektionsSchulen()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "director-fach-load", Email = "director.fach.load@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "teacher-fach-load", Email = "teacher.fach.load@test.local", Vorname = "Lia", Nachname = "Prof", FullName = "Lia Prof" };
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var otherSchool = new School { Id = Guid.NewGuid(), Name = "Autre école", City = "Kara" };
        var fachIncluded = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Chimie" };
        var fachExcluded = new Fach { Id = Guid.NewGuid(), SchoolId = otherSchool.Id, Name = "Arts" };

        db.Users.AddRange(director, teacher);
        db.Schools.AddRange(school, otherSchool);
        db.Faecher.AddRange(fachIncluded, fachExcluded);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = school.Id },
            new UserSchool { AppUserId = teacher.Id, SchoolId = otherSchool.Id });
        db.LehrerFachZuordnungen.AddRange(
            new LehrerFachZuordnung { LehrerId = teacher.Id, FachId = fachIncluded.Id },
            new LehrerFachZuordnung { LehrerId = teacher.Id, FachId = fachExcluded.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var model = await service.GetSchoolUserForEditAsync(director.Id, teacher.Id);

        Assert.NotNull(model);
        Assert.Single(model!.FachIds);
        Assert.Contains(fachIncluded.Id, model.FachIds);
    }

    [Fact]
    public async Task CreateFachForSchoolByDirectionAsync_ErstelltNurInEigenerSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter-create-fach", Email = "leiter.create.fach@test.local" };
        var ownSchool = new School { Id = Guid.NewGuid(), Name = "École Direction", City = "Lomé" };
        var foreignSchool = new School { Id = Guid.NewGuid(), Name = "École Externe", City = "Kara" };

        db.Users.Add(director);
        db.Schools.AddRange(ownSchool, foreignSchool);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = ownSchool.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var denied = await service.CreateFachForSchoolByDirectionAsync(director.Id, foreignSchool.Id, "Sciences");
        var created = await service.CreateFachForSchoolByDirectionAsync(director.Id, ownSchool.Id, "Sciences");

        Assert.False(denied.Succeeded);
        Assert.True(created.Succeeded, created.Message);
        Assert.True(await db.Faecher.AnyAsync(fach => fach.SchoolId == ownSchool.Id && fach.Name == "Sciences"));
    }

    [Fact]
    public async Task GetSchoolFachOptionsForDirectionAsync_MarkiertZugewieseneFaecher()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter-fach-list", Email = "leiter.fach.list@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer-fach-list", Email = "lehrer.fach.list@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École Direction", City = "Lomé" };
        var fachAssigned = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Mathématiques" };
        var fachFree = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Histoire" };

        db.Users.AddRange(director, teacher);
        db.Schools.Add(school);
        db.Faecher.AddRange(fachAssigned, fachFree);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = teacher.Id, SchoolId = school.Id });
        db.LehrerFachZuordnungen.Add(new LehrerFachZuordnung { LehrerId = teacher.Id, FachId = fachAssigned.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.GetSchoolFachOptionsForDirectionAsync(director.Id, school.Id);

        var assigned = Assert.Single(result, item => item.FachId == fachAssigned.Id);
        Assert.True(assigned.IstLehrerZugewiesen);
        var free = Assert.Single(result, item => item.FachId == fachFree.Id);
        Assert.False(free.IstLehrerZugewiesen);
    }

    [Fact]
    public async Task DeleteSchoolFachForDirectionAsync_LoeschtNurUnzugewiesenesFach()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter-delete-fach", Email = "leiter.delete.fach@test.local" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer-delete-fach", Email = "lehrer.delete.fach@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École Direction", City = "Lomé" };
        var fachAssigned = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Mathématiques" };
        var fachFree = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Histoire" };

        db.Users.AddRange(director, teacher);
        db.Schools.Add(school);
        db.Faecher.AddRange(fachAssigned, fachFree);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        db.LehrerFachZuordnungen.Add(new LehrerFachZuordnung { LehrerId = teacher.Id, FachId = fachAssigned.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, teacher]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(teacher, Rollen.Lehrer);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var denied = await service.DeleteSchoolFachForDirectionAsync(director.Id, fachAssigned.Id);
        var deleted = await service.DeleteSchoolFachForDirectionAsync(director.Id, fachFree.Id);

        Assert.False(denied.Succeeded);
        Assert.True(await db.Faecher.AnyAsync(fach => fach.Id == fachAssigned.Id));
        Assert.True(deleted.Succeeded, deleted.Message);
        Assert.False(await db.Faecher.AnyAsync(fach => fach.Id == fachFree.Id));
    }

    [Fact]
    public async Task CreateSchoolForSchoolLeaderAsync_ErstelltUndOrdnetDieSchuleDemSchulleiterZu()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.CreateSchoolForSchoolLeaderAsync(director.Id, new SchoolFormModel
        {
            Name = "École du directeur",
            City = "Lomé",
            Country = "Togo"
        });

        Assert.True(result.Succeeded, result.Message);
        var school = await db.Schools.SingleAsync();
        Assert.Equal("École du directeur", school.Name);
        Assert.True(await db.UserSchools.AnyAsync(link =>
            link.AppUserId == director.Id && link.SchoolId == school.Id && link.CanManageStudents));
    }

    [Fact]
    public async Task GetSchoolLeaderDataAsync_LiefertAlleEigenenSchulenOhneLehrerNiveauZuordnung()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var firstSchool = new School { Id = Guid.NewGuid(), Name = "École Douala", City = "Douala" };
        var secondSchool = new School { Id = Guid.NewGuid(), Name = "École Yaoundé", City = "Yaoundé" };
        var foreignSchool = new School { Id = Guid.NewGuid(), Name = "École Bafoussam", City = "Bafoussam" };
        var firstStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Douala", UserName = "douala" };
        var secondStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Yaoundé", UserName = "yaounde" };
        var foreignStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Bafoussam", UserName = "bafoussam" };

        db.Schools.AddRange(firstSchool, secondSchool, foreignSchool);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = firstSchool.Id, CanManageStudents = true },
            new UserSchool { AppUserId = director.Id, SchoolId = secondSchool.Id, CanManageStudents = true });
        db.Users.AddRange(firstStudent, secondStudent, foreignStudent);
        db.StudentProfiles.AddRange(
            new StudentProfile { AppUserId = firstStudent.Id, SchoolId = firstSchool.Id, CurrentLevelCode = "CM1A" },
            new StudentProfile { AppUserId = secondStudent.Id, SchoolId = secondSchool.Id, CurrentLevelCode = "CM2A" },
            new StudentProfile { AppUserId = foreignStudent.Id, SchoolId = foreignSchool.Id, CurrentLevelCode = "6eA" });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, firstStudent, secondStudent, foreignStudent]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var schools = await service.GetSchoolLeaderSchoolsAsync(director.Id);
        var students = await service.GetSchoolLeaderStudentsAsync(director.Id);

        Assert.Equal(2, schools.Count);
        Assert.Contains(schools, school => school.SchoolId == firstSchool.Id);
        Assert.Contains(schools, school => school.SchoolId == secondSchool.Id);
        Assert.Equal(2, students.Count);
        Assert.Contains(students, student => student.UserId == firstStudent.Id);
        Assert.Contains(students, student => student.UserId == secondStudent.Id);
    }

    [Fact]
    public async Task GetSchoolLeaderStudentsAsync_KennzeichnetLetzteFruehereKlasseUndNeueSchueler()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École Lomé" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter" };
        var formerStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Ancien élève", UserName = "ancien" };
        var newStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Nouvel élève", UserName = "nouveau" };
        var formerClass = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1A", Title = "CP1 A" };
        db.Schools.Add(school);
        db.Klassen.Add(formerClass);
        db.Users.AddRange(director, formerStudent, newStudent);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        db.StudentProfiles.AddRange(
            new StudentProfile { AppUserId = formerStudent.Id, SchoolId = school.Id },
            new StudentProfile { AppUserId = newStudent.Id, SchoolId = school.Id });
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = formerStudent.Id,
            KlasseId = formerClass.Id,
            IstAktuell = false
        });
        await db.SaveChangesAsync();
        var userManager = TestUserManager.Create([director, formerStudent, newStudent]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var students = await service.GetSchoolLeaderStudentsAsync(director.Id);

        var former = Assert.Single(students, student => student.UserId == formerStudent.Id);
        Assert.Equal(formerClass.Id, former.PreviousKlasseId);
        Assert.Equal("CP1A · CP1 A", former.PreviousKlasseName);
        var newlyRegistered = Assert.Single(students, student => student.UserId == newStudent.Id);
        Assert.Null(newlyRegistered.PreviousKlasseId);
        Assert.Empty(newlyRegistered.PreviousKlasseName);
    }

    [Fact]
    public async Task GetSchoolLeaderStudentsAsync_WiederholtIdentischenKlassencodeUndTitelNicht()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École Lomé" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter" };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève", UserName = "eleve" };
        var formerClass = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1B", Title = "CP1B" };
        db.Schools.Add(school);
        db.Klassen.Add(formerClass);
        db.Users.AddRange(director, student);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = formerClass.Id, IstAktuell = false });
        await db.SaveChangesAsync();
        var userManager = TestUserManager.Create([director, student]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = Assert.Single(await service.GetSchoolLeaderStudentsAsync(director.Id));

        Assert.Equal("CP1B", result.PreviousKlasseName);
    }

    [Fact]
    public async Task RemoveStudentFromKlasseAsync_NimmtUnbezahlteFehlzuordnungZurueck()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École Lomé" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter" };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève", UserName = "eleve" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1A", Title = "CP1A" };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Users.AddRange(director, student);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentKlasseId = klasse.Id, CurrentLevelCode = klasse.Code });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = klasse.Id, IstAktuell = true });
        await db.SaveChangesAsync();
        var userManager = TestUserManager.Create([director, student]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.RemoveStudentFromKlasseAsync(director.Id, student.Id, klasse.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(db.StudentProfiles.Single().CurrentKlasseId);
        Assert.False(db.LevelEnrollments.Single().IstAktuell);
        Assert.Equal(0, Assert.Single(await service.GetSchoolKlassenAsync(school.Id)).CurrentStudentCount);
    }

    [Fact]
    public async Task RemoveStudentFromKlasseAsync_VerweigertAktivesAbo()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École Lomé" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter" };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève", UserName = "eleve" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1A", Title = "CP1A" };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Users.AddRange(director, student);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentKlasseId = klasse.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = klasse.Id, IstAktuell = true, IsUnlocked = true });
        await db.SaveChangesAsync();
        var userManager = TestUserManager.Create([director, student]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.RemoveStudentFromKlasseAsync(director.Id, student.Id, klasse.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(klasse.Id, db.StudentProfiles.Single().CurrentKlasseId);
        Assert.True(db.LevelEnrollments.Single().IstAktuell);
    }

    [Fact]
    public async Task DeletePendingSelfRegisteredStudentAsync_LoeschtNurOffeneSelbstregistrierungDerEigenenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "Eigene Schule" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var student = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "neuerschueler",
            Email = "neuerschueler@test.local",
            IsSelfRegistered = true,
            IsBlocked = true
        };

        db.Schools.Add(school);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = student.Id, SchoolId = school.Id });
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentLevelCode = "CM1A" });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, student]);
        userManager.SeedRole(director, "SchulLeiter");
        userManager.SeedRole(student, "Schüler");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService(), new TestHostEnvironment("Development"));

        var result = await service.DeletePendingSelfRegisteredStudentAsync(director.Id, student.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.Null(await userManager.FindByIdAsync(student.Id.ToString()));
        Assert.False(await db.StudentProfiles.AnyAsync(profile => profile.AppUserId == student.Id));
        Assert.False(await db.UserSchools.AnyAsync(link => link.AppUserId == student.Id));
    }

    [Fact]
    public async Task DeletePendingSelfRegisteredStudentAsync_LehntBereitsBestaetigtenSchuelerAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "Eigene Schule" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var student = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "bestaetigt",
            Email = "bestaetigt@test.local",
            IsSelfRegistered = true,
            IsBlocked = false
        };

        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentLevelCode = "CM1A" });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, student]);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService(), new TestHostEnvironment("Development"));

        var result = await service.DeletePendingSelfRegisteredStudentAsync(director.Id, student.Id);

        Assert.False(result.Succeeded);
        Assert.NotNull(await userManager.FindByIdAsync(student.Id.ToString()));
    }

    [Fact]
    public async Task HardDeleteSchoolLeaderForDevelopmentAsync_EntferntSchuleUndExklusiveTestdaten_ErhaeltGeteilteBenutzer()
    {
        using var db = TestDbContext.CreateInMemory();
        var deletedSchool = new School { Id = Guid.NewGuid(), Name = "Testschule löschen" };
        var retainedSchool = new School { Id = Guid.NewGuid(), Name = "Testschule behalten" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = deletedSchool.Id, Code = "TEST-1", Title = "Testklasse" };
        var schoolLeader = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var exclusiveStudent = new AppUser { Id = Guid.NewGuid(), UserName = "schueler", Email = "schueler@test.local" };
        var sharedTeacher = new AppUser { Id = Guid.NewGuid(), UserName = "lehrer", Email = "lehrer@test.local" };

        db.Schools.AddRange(deletedSchool, retainedSchool);
        db.Klassen.Add(klasse);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = schoolLeader.Id, SchoolId = deletedSchool.Id, CanManageStudents = true },
            new UserSchool { AppUserId = sharedTeacher.Id, SchoolId = deletedSchool.Id, CanManageStudents = true },
            new UserSchool { AppUserId = sharedTeacher.Id, SchoolId = retainedSchool.Id, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = exclusiveStudent.Id,
            SchoolId = deletedSchool.Id,
            CurrentLevelCode = klasse.Code
        });
        var jahr = new Schuljahr { Id = Guid.NewGuid(), SchulID = deletedSchool.Id, Bezeichnung = "2026–2027" };
        var fach = new Fach { Id = Guid.NewGuid(), SchoolId = deletedSchool.Id, Name = "Mathématiques" };
        var zuordnung = new SchuelerJahresZuordnung { Id = Guid.NewGuid(), SchulID = deletedSchool.Id,
            SchuljahrID = jahr.Id, KlasseID = klasse.Id, SchuelerID = exclusiveStudent.Id };
        var periode = new BewertungsPeriode { Id = Guid.NewGuid(), SchulID = deletedSchool.Id, SchuljahrID = jahr.Id, Titel = "Trimestre" };
        var komponente = new BewertungsBestandteil { Id = Guid.NewGuid(), PeriodeID = periode.Id, Bezeichnung = "Composition" };
        var bewertung = new FachBewertung { Id = Guid.NewGuid(), SchuljahrID = jahr.Id, KlasseID = klasse.Id, FachID = fach.Id };
        var unterricht = new UnterrichtsZuordnung { Id = Guid.NewGuid(), SchulID = deletedSchool.Id, SchuljahrID = jahr.Id,
            KlasseID = klasse.Id, FachID = fach.Id, LehrerID = sharedTeacher.Id };
        var plan = new Stundenplan { Id = Guid.NewGuid(), SchulID = deletedSchool.Id, SchuljahrID = jahr.Id, KlasseID = klasse.Id };
        db.AddRange(jahr, fach, zuordnung, periode, komponente, bewertung, unterricht, plan);
        db.Add(new StundenplanEintrag { Id = Guid.NewGuid(), StundenplanID = plan.Id, UnterrichtsZuordnungID = unterricht.Id });
        db.Add(new FachNote { Id = Guid.NewGuid(), BestandteilID = komponente.Id, FachBewertungID = bewertung.Id, JahresZuordnungID = zuordnung.Id });
        db.Add(new BulletinAusgabe { Id = Guid.NewGuid(), SchulID = deletedSchool.Id, PeriodeID = periode.Id, JahresZuordnungID = zuordnung.Id });
        db.Add(new BulletinVorlage { Id = Guid.NewGuid(), SchulID = deletedSchool.Id, KlasseID = klasse.Id });
        db.Add(new AnwesenheitsEintrag { Id = Guid.NewGuid(), PeriodeID = periode.Id, JahresZuordnungID = zuordnung.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([schoolLeader, exclusiveStudent, sharedTeacher]);
        userManager.SeedRole(schoolLeader, "SchulLeiter");
        userManager.SeedRole(exclusiveStudent, "Schüler");
        userManager.SeedRole(sharedTeacher, "Lehrer");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService(), new TestHostEnvironment("Development"));

        var result = await service.HardDeleteSchoolLeaderForDevelopmentAsync(schoolLeader.Id);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(await db.Schools.AnyAsync(school => school.Id == deletedSchool.Id));
        Assert.True(await db.Schools.AnyAsync(school => school.Id == retainedSchool.Id));
        Assert.False(await db.Klassen.AnyAsync(item => item.Id == klasse.Id));
        Assert.False(await db.StudentProfiles.AnyAsync(profile => profile.AppUserId == exclusiveStudent.Id));
        Assert.Empty(await db.UserSchools.Where(link => link.SchoolId == deletedSchool.Id).ToListAsync());
        Assert.NotNull(await userManager.FindByIdAsync(sharedTeacher.Id.ToString()));
        Assert.Null(await userManager.FindByIdAsync(schoolLeader.Id.ToString()));
        Assert.Null(await userManager.FindByIdAsync(exclusiveStudent.Id.ToString()));
        Assert.True(await db.UserSchools.AnyAsync(link => link.AppUserId == sharedTeacher.Id && link.SchoolId == retainedSchool.Id));
        Assert.Empty(await db.Set<Schuljahr>().ToListAsync());
        Assert.Empty(await db.Set<BulletinAusgabe>().ToListAsync());
        Assert.Empty(await db.Set<FachNote>().ToListAsync());
        Assert.Empty(await db.Set<AnwesenheitsEintrag>().ToListAsync());
        Assert.Empty(await db.Set<StundenplanEintrag>().ToListAsync());
    }

    [Fact]
    public async Task HardDeleteSchoolLeaderForDevelopmentAsync_AusserhalbVonDevelopment_VerweigertDieLoeschung()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "Produktionsschule" };
        var schoolLeader = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@production.local" };
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = schoolLeader.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([schoolLeader]);
        userManager.SeedRole(schoolLeader, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService(), new TestHostEnvironment("Production"));

        var result = await service.HardDeleteSchoolLeaderForDevelopmentAsync(schoolLeader.Id);

        Assert.False(result.Succeeded);
        Assert.True(await db.Schools.AnyAsync(item => item.Id == school.Id));
        Assert.NotNull(await userManager.FindByIdAsync(schoolLeader.Id.ToString()));
    }

    [Fact]
    public async Task GetAdminSummaryAsync_LiefertKorrekteKennzahlen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();

        db.Schools.Add(new School { Id = Guid.NewGuid(), Name = "Schule A", IsActive = true });
        db.Schools.Add(new School { Id = Guid.NewGuid(), Name = "Schule B", IsActive = false });
        db.Klassen.Add(new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "Anfänger" });
        await db.SaveChangesAsync();

        var teacher = new AppUser { Id = Guid.NewGuid(), FullName = "Lehrerin", UserName = "lehrer" };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Schüler", UserName = "schueler" };
        var blocked = new AppUser { Id = Guid.NewGuid(), FullName = "Gesperrt", UserName = "gesperrt", IsBlocked = true };

        var userManager = TestUserManager.Create(new[] { teacher, student, blocked });
        userManager.SeedRole(teacher, "Lehrer");
        userManager.SeedRole(student, "Schüler");
        userManager.SeedRole(blocked, "Schüler");

        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var summary = await service.GetAdminSummaryAsync();

        // Assert
        Assert.Equal(2, summary.SchoolCount);
        Assert.Equal(1, summary.ActiveSchoolCount);
        Assert.Equal(1, summary.TeacherCount);
        Assert.Equal(2, summary.StudentCount);
        Assert.Equal(1, summary.BlockedStudentCount);
        Assert.Equal(2, summary.CourseCount);
    }

    [Fact]
    public async Task GetAdminSchoolsAsync_LiefertSchulenAlphabetischSortiert()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        db.Schools.Add(new School { Id = Guid.NewGuid(), Name = "Zebra", IsActive = true });
        db.Schools.Add(new School { Id = Guid.NewGuid(), Name = "Alpha", IsActive = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create();
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var schools = await service.GetAdminSchoolsAsync();

        // Assert
        Assert.Equal(2, schools.Count);
        Assert.Equal("Alpha", schools[0].Name);
        Assert.Equal("Zebra", schools[1].Name);
    }

    [Fact]
    public async Task GetTeacherStudentProgressAsync_MitNiveauZuordnung_LiefertNurZugeordneteSchueler()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schuleId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        db.Schools.Add(new School { Id = schuleId, Name = "École Test", IsActive = true });
        db.UserSchools.Add(new UserSchool { AppUserId = teacherId, SchoolId = schuleId, CanManageStudents = true });
        var klasseCM1 = new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "Débutant" };
        var klasseCM2 = new Klasse { Id = Guid.NewGuid(), Code = "CM2A", Title = "Élémentaire" };
        db.Klassen.AddRange(klasseCM1, klasseCM2);

        var schuelerA = new AppUser { Id = Guid.NewGuid(), FullName = "Élève A", UserName = "eleveA" };
        var schuelerB = new AppUser { Id = Guid.NewGuid(), FullName = "Élève B", UserName = "eleveB" };
        db.Users.AddRange(schuelerA, schuelerB);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerA.Id, SchoolId = schuleId, CurrentLevelCode = "CM1A", CurrentKlasseId = klasseCM1.Id });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerB.Id, SchoolId = schuleId, CurrentLevelCode = "CM2A", CurrentKlasseId = klasseCM2.Id });

        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = teacherId, SchulID = schuleId, KlasseId = klasseCM1.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schuelerA, schuelerB });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var progress = await service.GetTeacherStudentProgressAsync(teacherId);

        // Assert
        var eintrag = Assert.Single(progress);
        Assert.Equal("Élève A", eintrag.FullName);
        Assert.Equal("CM1A", eintrag.CurrentLevelCode);
    }

    [Fact]
    public async Task GetTeacherStudentProgressAsync_OhneZuordnung_LiefertKeineSchueler()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schuleId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        db.Schools.Add(new School { Id = schuleId, Name = "École Test", IsActive = true });
        db.UserSchools.Add(new UserSchool { AppUserId = teacherId, SchoolId = schuleId, CanManageStudents = true });

        var schuelerA = new AppUser { Id = Guid.NewGuid(), FullName = "Élève A", UserName = "eleveA" };
        var schuelerB = new AppUser { Id = Guid.NewGuid(), FullName = "Élève B", UserName = "eleveB" };
        db.Users.AddRange(schuelerA, schuelerB);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerA.Id, SchoolId = schuleId, CurrentLevelCode = "CM1A" });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerB.Id, SchoolId = schuleId, CurrentLevelCode = "CM2A" });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schuelerA, schuelerB });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var progress = await service.GetTeacherStudentProgressAsync(teacherId);

        // Assert
        Assert.Empty(progress);
    }

    [Fact]
    public async Task GetStudentDashboardAsync_ZeigtLetzteAktivitaetUndNurAktuelleSerieDerKlasse()
    {
        using var db = TestDbContext.CreateInMemory();
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "student-activity", FullName = "Élève" };
        var school = new School { Id = Guid.NewGuid(), Name = "École Test", IsActive = true };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1A", Title = "CP1A" };
        var otherKlasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1B", Title = "CP1B" };
        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", Order = 1 };
        var otherLesson = new Lesson { Id = Guid.NewGuid(), KlasseId = otherKlasse.Id, Title = "Autre leçon", Order = 1 };
        var exercise = new Exercise { Id = Guid.NewGuid(), LessonId = lesson.Id, Title = "Exercice", Order = 1 };
        var otherExercise = new Exercise { Id = Guid.NewGuid(), LessonId = otherLesson.Id, Title = "Autre exercice", Order = 1 };
        db.Users.Add(student);
        db.Schools.Add(school);
        db.Klassen.AddRange(klasse, otherKlasse);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = student.Id, SchoolId = school.Id, CurrentKlasseId = klasse.Id, CurrentLevelCode = klasse.Code });
        db.Lessons.AddRange(lesson, otherLesson);
        db.Exercises.AddRange(exercise, otherExercise);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create([student]), new FakeMediaStorageService());
        var empty = await service.GetStudentDashboardAsync(student.Id);
        Assert.NotNull(empty);
        Assert.Equal(0, empty.StreakDays);
        Assert.Null(empty.LastActivityAtUtc);

        var today = DateTime.UtcNow.Date;
        var lastWeek = today.AddDays(-7).AddHours(10);
        db.ExerciseAttempts.AddRange(
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = exercise.Id, StudentId = student.Id, AnsweredAtUtc = lastWeek },
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = otherExercise.Id, StudentId = student.Id, AnsweredAtUtc = today.AddHours(12) });
        await db.SaveChangesAsync();

        var oldActivity = await service.GetStudentDashboardAsync(student.Id);
        Assert.NotNull(oldActivity);
        Assert.Equal(0, oldActivity.StreakDays);
        Assert.Equal(lastWeek, oldActivity.LastActivityAtUtc);

        var yesterday = today.AddDays(-1).AddHours(11);
        var latest = today.AddHours(9);
        db.ExerciseAttempts.Add(new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = exercise.Id, StudentId = student.Id, AnsweredAtUtc = yesterday });
        db.LessonProgresses.Add(new LessonProgress { Id = Guid.NewGuid(), LessonId = lesson.Id, StudentId = student.Id, IsCompleted = true, CompletedAtUtc = latest });
        await db.SaveChangesAsync();

        var active = await service.GetStudentDashboardAsync(student.Id);
        Assert.NotNull(active);
        Assert.Equal(2, active.StreakDays);
        Assert.Equal(latest, active.LastActivityAtUtc);
    }

    [Fact]
    public async Task GetTeacherStudentsAsync_BerechnetKombiniertenScoreAusUebungenUndPruefungen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schuleId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();

        db.Schools.Add(new School { Id = schuleId, Name = "École Test", IsActive = true });
        db.UserSchools.Add(new UserSchool { AppUserId = teacherId, SchoolId = schuleId, CanManageStudents = true });

        var schueler = new AppUser { Id = Guid.NewGuid(), FullName = "Élève A", UserName = "eleveA" };
        db.Users.Add(schueler);
        var levelCM1 = new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "Débutant" };
        db.Klassen.Add(levelCM1);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schueler.Id, SchoolId = schuleId, CurrentLevelCode = "CM1A", CurrentKlasseId = levelCM1.Id });
        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = teacherId, SchulID = schuleId, KlasseId = levelCM1.Id });

        // Übungsversuche: eine richtig (100), eine falsch (0). Ein offener manueller Versuch zählt nicht.
        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = levelCM1.Id, Title = "Leçon", Order = 1 };
        db.Lessons.Add(lesson);
        var exercises = Enumerable.Range(0, 3).Select(i => new Exercise { Id = Guid.NewGuid(), LessonId = lesson.Id, Title = $"Exercice {i}", Order = i }).ToArray();
        db.Exercises.AddRange(exercises);
        db.ExerciseAttempts.AddRange(
            new ExerciseAttempt { ExerciseId = exercises[0].Id, StudentId = schueler.Id, IsCorrect = true, RequiresManualGrading = false },
            new ExerciseAttempt { ExerciseId = exercises[1].Id, StudentId = schueler.Id, IsCorrect = false, RequiresManualGrading = false },
            new ExerciseAttempt { ExerciseId = exercises[2].Id, StudentId = schueler.Id, IsCorrect = false, RequiresManualGrading = true });

        // Prüfungsversuch: abgeschlossen, automatisch bewertet mit 80 %. Ein nicht abgeschlossener zählt nicht.
        var veroeffentlichtesExamId = Guid.NewGuid();
        var nichtVeroeffentlichtesExamId = Guid.NewGuid();
        db.Exams.AddRange(
            new Exam
            {
                Id = veroeffentlichtesExamId,
                KlasseId = levelCM1.Id,
                Title = "Examen publié",
                DurationInMinutes = 30,
                PassingScore = 50,
                IsPublishedToStudents = true
            },
            new Exam
            {
                Id = nichtVeroeffentlichtesExamId,
                KlasseId = levelCM1.Id,
                Title = "Examen non publié",
                DurationInMinutes = 30,
                PassingScore = 50,
                IsPublishedToStudents = false
            });

        db.ExamAttempts.AddRange(
            new ExamAttempt { ExamId = veroeffentlichtesExamId, StudentId = schueler.Id, IsCompleted = true, RequiresManualGrading = false, ScorePercent = 80 },
            new ExamAttempt { ExamId = nichtVeroeffentlichtesExamId, StudentId = schueler.Id, IsCompleted = false, RequiresManualGrading = false, ScorePercent = 20 });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schueler });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var students = await service.GetTeacherStudentsAsync(teacherId);

        // Assert
        var eintrag = Assert.Single(students);
        // Kombiniert: (100 + 0 + 80) / 3 = 60 %, 3 zählende Versuche.
        Assert.Equal(60, eintrag.CombinedScorePercent);
        Assert.Equal(3, eintrag.CombinedScoreCount);
    }

    [Fact]
    public async Task GetStudentDashboardAsync_ZaehltNurVeroeffentlichteAufgabenUndLetztenVersuchJeAufgabe()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École Test", IsActive = true };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève", UserName = "eleve" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1A", Title = "CP1A" };
        var publishedLesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Visible", IsPublished = true };
        var hiddenLesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Masquée", IsPublished = false };
        var firstExercise = new Exercise { Id = Guid.NewGuid(), LessonId = publishedLesson.Id, Title = "Exercice 1", Points = 10 };
        var secondExercise = new Exercise { Id = Guid.NewGuid(), LessonId = publishedLesson.Id, Title = "Exercice 2", Points = 10 };
        var hiddenExercise = new Exercise { Id = Guid.NewGuid(), LessonId = hiddenLesson.Id, Title = "Ancien exercice", Points = 10 };
        db.AddRange(school, student, klasse, publishedLesson, hiddenLesson, firstExercise, secondExercise, hiddenExercise);
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = student.Id,
            SchoolId = school.Id,
            CurrentKlasseId = klasse.Id,
            CurrentLevelCode = klasse.Code
        });
        db.ExerciseAttempts.AddRange(
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = firstExercise.Id, StudentId = student.Id, IsCorrect = false, AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-3) },
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = firstExercise.Id, StudentId = student.Id, IsCorrect = true, AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-2) },
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = secondExercise.Id, StudentId = student.Id, IsCorrect = true, AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-1) },
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = hiddenExercise.Id, StudentId = student.Id, IsCorrect = true, AnsweredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new DashboardService(db, TestUserManager.Create([student]), new FakeMediaStorageService());

        var dashboard = await service.GetStudentDashboardAsync(student.Id);

        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.CombinedScoreCount);
        Assert.Equal(100, dashboard.CombinedScorePercent);
        Assert.Equal(20, dashboard.ExercisePointsEarned);
        Assert.Equal(20, dashboard.ExercisePointsTotal);
    }

    [Fact]
    public async Task SaveSchoolAsync_PersistiertSchulSpezifischePreise()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var userManager = TestUserManager.Create();
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var form = new SchoolFormModel
        {
            Name = "École Prix",
            City = "Yaoundé",
            Country = "Cameroun",
            IsActive = true,
            SubscriptionPriceOverride = 15000m
        };

        // Act
        var result = await service.SaveSchoolAsync(form, await TestIdentityFactory.AddSuperAdminAsync(db));

        // Assert
        Assert.True(result.Succeeded);
        var school = Assert.Single(db.Schools);
        Assert.Equal(15000m, school.SubscriptionPriceOverride);
        Assert.Equal(15000m, school.EffektiverPreis(AboTyp.SkoulouApp));
        Assert.Equal(15000m, school.EffektiverPreis(AboTyp.SkoulouApp));
    }

    [Fact]
    public async Task GetSchoolForEditAsync_OhneUeberschreibung_LiefertStandardpreise()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schuleId = Guid.NewGuid();
        db.Schools.Add(new School
        {
            Id = schuleId,
            Name = "École Défaut",
            City = "Douala",
            Country = "Cameroun",
            IsActive = true,
            SubscriptionPriceOverride = null
        });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create();
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var form = await service.GetSchoolForEditAsync(schuleId);

        // Assert
        Assert.NotNull(form);
        Assert.Null(form!.SubscriptionPriceOverride);
    }

    [Fact]
    public async Task SaveSchoolAsync_UebernimmtKeineNeuenSchulprovisionen()
    {
        using var db = TestDbContext.CreateInMemory();
        var userManager = TestUserManager.Create();
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var form = new SchoolFormModel
        {
            Name = "École Commission",
            City = "Yaoundé",
            Country = "Cameroun",
            IsActive = true,
            SchoolLeaderCommissionPercentOverride = 6m,
            TeacherCommissionPercentOverride = 8m,
            ProductOwnerCommissionPercentOverride = 2m
        };

        var result = await service.SaveSchoolAsync(form, await TestIdentityFactory.AddSuperAdminAsync(db));

        Assert.True(result.Succeeded, result.Message);
        var school = Assert.Single(db.Schools);
        Assert.Null(school.SchoolLeaderCommissionPercentOverride);
        Assert.Null(school.TeacherCommissionPercentOverride);
        Assert.Null(school.ProductOwnerCommissionPercentOverride);
    }

    [Fact]
    public async Task SaveSchoolAsync_IgnoriertNichtMehrBearbeitbareProvisionsfelder()
    {
        using var db = TestDbContext.CreateInMemory();
        var userManager = TestUserManager.Create();
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var form = new SchoolFormModel
        {
            Name = "École Invalide",
            City = "Douala",
            Country = "Cameroun",
            IsActive = true,
            SchoolLeaderCommissionPercentOverride = 60m,
            TeacherCommissionPercentOverride = 30m,
            ProductOwnerCommissionPercentOverride = 20m
        };

        var result = await service.SaveSchoolAsync(form, await TestIdentityFactory.AddSuperAdminAsync(db));

        Assert.True(result.Succeeded, result.Message);
        var school = Assert.Single(db.Schools);
        Assert.Null(school.SchoolLeaderCommissionPercentOverride);
        Assert.Null(school.TeacherCommissionPercentOverride);
    }

    [Fact]
    public async Task SaveSchoolAsync_AendertKeineLehrerOderDirektionsZuordnungen()
    {
        using var db = TestDbContext.CreateInMemory();

        var schoolId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var direktorId = Guid.NewGuid();

        db.Schools.Add(new School
        {
            Id = schoolId,
            Name = "École A",
            City = "Lomé",
            Country = "Togo",
            IsActive = true
        });

        db.Users.AddRange(
            new AppUser { Id = teacherId, FullName = "Prof A", UserName = "prof.a", Email = "prof.a@skoulou.com" },
            new AppUser { Id = direktorId, FullName = "Directeur A", UserName = "directeur.a", Email = "directeur.a@skoulou.com" });

        db.UserSchools.AddRange(
            new UserSchool { AppUserId = teacherId, SchoolId = schoolId, CanManageStudents = true },
            new UserSchool { AppUserId = direktorId, SchoolId = schoolId, CanManageStudents = true });

        await db.SaveChangesAsync();

        var teacherUser = new AppUser { Id = teacherId, FullName = "Prof A", UserName = "prof.a", Email = "prof.a@skoulou.com" };
        var direktorUser = new AppUser { Id = direktorId, FullName = "Directeur A", UserName = "directeur.a", Email = "directeur.a@skoulou.com" };
        var userManager = TestUserManager.Create(new[] { teacherUser, direktorUser });
        userManager.SeedRole(teacherUser, Rollen.Lehrer);
        userManager.SeedRole(direktorUser, Rollen.SchulLeiter);

        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var form = new SchoolFormModel
        {
            Id = schoolId,
            Name = "École A",
            City = "Lomé",
            Country = "Togo",
            IsActive = true
        };

        var result = await service.SaveSchoolAsync(form, await TestIdentityFactory.AddSuperAdminAsync(db));

        Assert.True(result.Succeeded, result.Message);

        var links = db.UserSchools.Where(link => link.SchoolId == schoolId && link.CanManageStudents).ToList();
        // Die Schulstammdaten des SuperAdmins ändern keine Personalzuordnungen (Lehrer verwaltet die Direktion).
        Assert.Contains(links, link => link.AppUserId == teacherId);
        Assert.Contains(links, link => link.AppUserId == direktorId);
    }

    [Fact]
    public async Task SetStudentBlockedStateAsync_ValidiertGesperrtenSchueler()
    {
        // Arrange (Testliste 4.2: Schüler freischalten)
        using var db = TestDbContext.CreateInMemory();
        var schueler = new AppUser { Id = Guid.NewGuid(), FullName = "Élève A", UserName = "eleveA", IsBlocked = true };
        db.Users.Add(schueler);
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schueler });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        await service.SetStudentBlockedStateAsync(schueler.Id, false);

        // Assert
        var aktualisiert = Assert.Single(db.Users);
        Assert.False(aktualisiert.IsBlocked);
    }

    [Fact]
    public async Task SetStudentBlockedStateAsync_BlockiertValidiertenSchueler()
    {
        // Arrange (Testliste 4.3: Schüler wieder blockieren)
        using var db = TestDbContext.CreateInMemory();
        var schueler = new AppUser { Id = Guid.NewGuid(), FullName = "Élève B", UserName = "eleveB", IsBlocked = false };
        db.Users.Add(schueler);
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schueler });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        await service.SetStudentBlockedStateAsync(schueler.Id, true);

        // Assert
        var aktualisiert = Assert.Single(db.Users);
        Assert.True(aktualisiert.IsBlocked);
    }

    [Fact]
    public async Task SetSchoolUserBlockedStateAsync_KoordinatorKannFremdenSchuelerNichtBlockieren()
    {
        using var db = TestDbContext.CreateInMemory();
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "coord", Email = "coord@test.local" };
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "eleve", Email = "eleve@test.local" };
        var ownSchool = new School { Id = Guid.NewGuid(), Name = "École A" };
        var otherSchool = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.AddRange(coordinator, student);
        db.Schools.AddRange(ownSchool, otherSchool);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = coordinator.Id, SchoolId = ownSchool.Id, CanManageStudents = true },
            new UserSchool { AppUserId = student.Id, SchoolId = otherSchool.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([coordinator, student]);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        userManager.SeedRole(student, Rollen.Schueler);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SetSchoolUserBlockedStateAsync(coordinator.Id, student.Id, true);

        Assert.False(result.Succeeded);
        Assert.False(student.IsBlocked);
    }

    [Fact]
    public async Task SetSchoolUserBlockedStateAsync_KoordinatorDarfLeiterNichtBlockieren()
    {
        using var db = TestDbContext.CreateInMemory();
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "coord", Email = "coord@test.local" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        db.Users.AddRange(coordinator, director);
        db.Schools.Add(school);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = coordinator.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([coordinator, director]);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SetSchoolUserBlockedStateAsync(coordinator.Id, director.Id, true);

        Assert.False(result.Succeeded);
        Assert.False(director.IsBlocked);
    }

    [Fact]
    public async Task SetSchoolUserBlockedStateAsync_DirektorErhaeltFehlerBeiBezahltemSchueler()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "eleve", Email = "eleve@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        db.Users.AddRange(director, student);
        db.Schools.Add(school);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = student.Id, SchoolId = school.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = Guid.NewGuid(), IsPaid = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, student]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(student, Rollen.Schueler);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SetSchoolUserBlockedStateAsync(director.Id, student.Id, true);

        Assert.True(result.Succeeded);
        Assert.True(student.IsBlocked);
    }

    [Fact]
    public async Task SetStudentBlockedStateAsync_UnbekannterSchueler_BleibtOhneAuswirkung()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schueler = new AppUser { Id = Guid.NewGuid(), FullName = "Élève C", UserName = "eleveC", IsBlocked = false };
        db.Users.Add(schueler);
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schueler });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act (unbekannte ID darf nichts verändern und nicht werfen)
        await service.SetStudentBlockedStateAsync(Guid.NewGuid(), true);

        // Assert
        var unveraendert = Assert.Single(db.Users);
        Assert.False(unveraendert.IsBlocked);
    }

    [Fact]
    public async Task SetStudentBlockedStateAsync_BezahltesNiveau_SchulLeiterKannUnabhaengigVonZahlungBlockieren()
    {
        // Arrange (Punkt 7c: Schüler mit bezahltem Niveau darf vom Direktor nicht blockiert werden)
        using var db = TestDbContext.CreateInMemory();
        var schueler = new AppUser { Id = Guid.NewGuid(), FullName = "Élève D", UserName = "eleveD", IsBlocked = false };
        db.Users.Add(schueler);
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = schueler.Id,
            KlasseId = Guid.NewGuid(),
            IsUnlocked = true,
            IsPaid = true
        });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schueler });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act (Standard = Direktor)
        await service.SetStudentBlockedStateAsync(schueler.Id, true);

        // Persönliche Kontosperren sind unabhängig von Finanzpositionen.
        var aktualisiert = Assert.Single(db.Users);
        Assert.True(aktualisiert.IsBlocked);
    }

    [Fact]
    public async Task SetStudentBlockedStateAsync_BezahltesNiveau_SuperAdminKannBlockieren()
    {
        // Arrange (Punkt 7c: nur SuperAdmin darf einen bezahlten Schüler blockieren)
        using var db = TestDbContext.CreateInMemory();
        var schueler = new AppUser { Id = Guid.NewGuid(), FullName = "Élève E", UserName = "eleveE", IsBlocked = false };
        db.Users.Add(schueler);
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = schueler.Id,
            KlasseId = Guid.NewGuid(),
            IsUnlocked = true,
            IsPaid = true
        });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schueler });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act (SuperAdmin)
        await service.SetStudentBlockedStateAsync(schueler.Id, true, allowBlockingPaidStudent: true);

        // Assert
        var aktualisiert = Assert.Single(db.Users);
        Assert.True(aktualisiert.IsBlocked);
    }

    [Fact]
    public async Task GetUserManagementAsync_SetztHasPaidEnrollmentFuerBezahlteSchueler()
    {
        // Arrange (Punkt 7: Zahlungsstatus muss im UserManagement für SuperAdmin sichtbar sein)
        using var db = TestDbContext.CreateInMemory();
        var bezahlt = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Payé", UserName = "elevePaye" };
        var unbezahlt = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Impayé", UserName = "eleveImpaye" };
        db.Users.Add(bezahlt);
        db.Users.Add(unbezahlt);
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = bezahlt.Id,
            KlasseId = Guid.NewGuid(),
            IsUnlocked = true,
            IsPaid = true
        });
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = unbezahlt.Id,
            KlasseId = Guid.NewGuid(),
            IsUnlocked = true,
            IsPaid = false
        });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { bezahlt, unbezahlt });
        userManager.SeedRole(bezahlt, "Schüler");
        userManager.SeedRole(unbezahlt, "Schüler");

        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var result = await service.GetUserManagementAsync();

        // Assert
        Assert.True(result.Single(x => x.UserId == bezahlt.Id).HasPaidEnrollment);
        Assert.False(result.Single(x => x.UserId == unbezahlt.Id).HasPaidEnrollment);
    }

    [Fact]
    public async Task SaveCourseAsync_SystemNiveau_KannNichtBearbeitetWerden()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var systemNiveau = new Klasse
        {
            Id = Guid.NewGuid(),
            Code = "CM1A",
            Title = "Classe archivée",
            IstSystemNiveau = true,
            SchoolId = null
        };
        db.Klassen.Add(systemNiveau);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        // Act
        var (succeeded, _, _) = await service.SaveCourseAsync(new CourseFormModel
        {
            CourseId = systemNiveau.Id,
            Code = "CM1A",
            Title = "Geändert",
            Description = "x"
        });

        // Assert
        Assert.False(succeeded);
    }

    [Fact]
    public async Task DeleteSchoolKlasseAsync_SchulEigeneKlasse_WirdFuerSchulleiterAbgelehnt()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "Schule", IsActive = true };
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "Cours élémentaire", SchoolId = schule.Id };
        db.Schools.Add(schule);
        db.Klassen.Add(klasse);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        var (succeeded, message) = await service.DeleteSchoolKlasseAsync(schule.Id, klasse.Id);

        Assert.False(succeeded);
        Assert.Contains("pas les supprimer", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.Klassen.AnyAsync(item => item.Id == klasse.Id));
    }

    [Fact]
    public async Task DeleteKlassenStufeAsync_WirdFuerSchulleiterAbgelehnt()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "Schule", IsActive = true };
        var stufe = new KlassenStufe { Id = Guid.NewGuid(), SchoolId = schule.Id, Name = "CEP1" };
        db.Schools.Add(schule);
        db.KlassenStufen.Add(stufe);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        var (succeeded, message) = await service.DeleteKlassenStufeAsync(schule.Id, stufe.Id);

        Assert.False(succeeded);
        Assert.Contains("pas les supprimer", message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.KlassenStufen.AnyAsync(item => item.Id == stufe.Id));
    }

    [Fact]
    public async Task SaveSchoolKlasseAsync_LehntDoppelteKlassenNamenAb()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", IsActive = true };
        db.Schools.Add(schule);
        // Ein globales System-Niveau darf nicht mitzählen.
        db.Klassen.Add(new Klasse { Id = Guid.NewGuid(), Code = "CM1A", IstSystemNiveau = true, SchoolId = null });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        // Act
        var ersteKlasse = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Code = "EXAM", Title = "Préparation examen", ReferenceName = "Préparation examen", NeueKlassenStufe = "Préparation examen", Description = "x"
        });
        var zweiteKlasse = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Code = "EXAM2", Title = "Préparation examen", ReferenceName = "Préparation examen", NeueKlassenStufe = "Préparation examen", Description = "x"
        });

        // Assert
        Assert.True(ersteKlasse.Succeeded);
        Assert.False(zweiteKlasse.Succeeded);
    }

    [Fact]
    public async Task SaveSchoolKlasseForSchoolLeaderAsync_LehntFremdeSchuleAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var eigeneSchule = new School { Id = Guid.NewGuid(), Name = "Eigene Schule" };
        var fremdeSchule = new School { Id = Guid.NewGuid(), Name = "Fremde Schule" };
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        db.Schools.AddRange(eigeneSchule, fremdeSchule);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = eigeneSchule.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, "SchulLeiter");
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolKlasseForSchoolLeaderAsync(director.Id, fremdeSchule.Id, new CourseFormModel
        {
            Code = "6A", Title = "Sixième A", Description = "Classe", NeueKlassenStufe = "6e"
        });

        Assert.False(result.Succeeded);
        Assert.False(await db.Klassen.AnyAsync(klasse => klasse.SchoolId == fremdeSchule.Id && klasse.Code == "6A"));
    }

    [Fact]
    public async Task GetSchoolKlassenAsync_SchliesstGlobaleSystemNiveausAus()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", IsActive = true };
        db.Schools.Add(schule);
        db.Klassen.AddRange(
            new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "Standard", IstSystemNiveau = true, SchoolId = null },
            new Klasse { Id = Guid.NewGuid(), Code = "EXAM", Title = "Préparation", IstSystemNiveau = false, SchoolId = schule.Id });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        // Act
        var klassen = await service.GetSchoolKlassenAsync(schule.Id);

        // Assert
        var klasse = Assert.Single(klassen);
        Assert.Equal("EXAM", klasse.Code);
        Assert.False(klasse.IstSystemNiveau);
    }

    [Fact]
    public async Task SaveSchoolKlasseAsync_NormaleSchule_ErlaubtParallelklassenMitEinheitlichemNamen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "Gymnasium", IsActive = true };
        db.Schools.Add(schule);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        // Act
        var klasse1 = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Title = "CEP1A", NeueKlassenStufe = "CEP1", Description = "x"
        });
        var klasse2 = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Title = "CEP1B", NeueKlassenStufe = "CEP1", Description = "x"
        });

        // Assert
        Assert.True(klasse1.Succeeded);
        Assert.True(klasse2.Succeeded);
        Assert.Single(db.KlassenStufen);
        var klassen = db.Klassen.Where(klasse => klasse.SchoolId == schule.Id).OrderBy(klasse => klasse.Code).ToList();
        Assert.All(klassen, klasse => Assert.NotNull(klasse.KlassenStufeId));
        Assert.Equal(["CEP1A", "CEP1B"], klassen.Select(klasse => klasse.Code));
        Assert.All(klassen, klasse => Assert.Equal(klasse.Code, klasse.Title));
    }

    [Fact]
    public async Task DeleteSchoolKlasseAsync_SystemNiveau_WirdAbgelehnt()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "Schule", IsActive = true };
        db.Schools.Add(schule);
        var systemNiveau = new Klasse { Id = Guid.NewGuid(), Code = "6eA", IstSystemNiveau = true, SchoolId = null };
        db.Klassen.Add(systemNiveau);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        // Act – System-Niveau gehört keiner Schule, daher weder auffindbar noch löschbar
        var (succeeded, _) = await service.DeleteSchoolKlasseAsync(schule.Id, systemNiveau.Id);

        // Assert
        Assert.False(succeeded);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_KoordinatorWirdNurEigenenSchulenZugeordnet()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var ownSchool = new School { Id = Guid.NewGuid(), Name = "Eigene Schule" };
        var foreignSchool = new School { Id = Guid.NewGuid(), Name = "Fremde Schule" };
        db.Users.Add(director);
        db.Schools.AddRange(ownSchool, foreignSchool);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = ownSchool.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var permitted = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Claire",
            Nachname = "Coord",
            Email = "claire.coord@test.local",
            Password = "Start123!",
            RoleName = Rollen.Koordinator,
            SchoolIds = [ownSchool.Id]
        });

        var denied = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Paul",
            Nachname = "Coord",
            Email = "paul.coord@test.local",
            Password = "Start123!",
            RoleName = Rollen.Koordinator,
            SchoolIds = [foreignSchool.Id]
        });

        Assert.True(permitted.Succeeded, permitted.Message);
        Assert.False(denied.Succeeded);
        var coordinatorId = await db.UserSchools
            .Where(link => link.SchoolId == ownSchool.Id && link.AppUserId != director.Id)
            .Select(link => link.AppUserId)
            .SingleAsync();
        var coordinator = await userManager.FindByIdAsync(coordinatorId.ToString());
        Assert.NotNull(coordinator);
        Assert.Contains(Rollen.Koordinator, await userManager.GetRolesAsync(coordinator));
        Assert.True(await db.UserSchools.AnyAsync(link => link.AppUserId == coordinator.Id && link.SchoolId == ownSchool.Id && link.CanManageStudents));
    }

    [Fact]
    public async Task SaveSchoolUserAsync_SchuelerErhaeltKeinAutomatischesAbo()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        db.Users.Add(director);
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Esso",
            Nachname = "Test",
            Email = "esso.test@test.local",
            Password = "Start123!",
            RoleName = Rollen.Schueler,
            SchoolIds = [school.Id]
        });

        Assert.True(result.Succeeded, result.Message);
        var studentId = await db.UserSchools
            .Where(link => link.SchoolId == school.Id && link.AppUserId != director.Id)
            .Select(link => link.AppUserId)
            .SingleAsync();
        Assert.True(await db.StudentProfiles.AnyAsync(profile => profile.AppUserId == studentId && profile.SchoolId == school.Id));
        Assert.False(await db.LevelEnrollments.AnyAsync(enrollment => enrollment.StudentId == studentId));
    }

    [Fact]
    public async Task SaveSchoolUserAsync_SchuelerMitKlasseErhaeltTechnischeEinschreibungOhneAbo()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "CM1", SchoolId = school.Id, IstSystemNiveau = false };
        db.Users.Add(director);
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Afi",
            Nachname = "Classe",
            Email = "afi.classe@test.local",
            Password = "Start123!",
            RoleName = Rollen.Schueler,
            SchoolIds = [school.Id],
            CurrentKlasseId = klasse.Id
        });

        Assert.True(result.Succeeded, result.Message);
        var studentId = await db.UserSchools
            .Where(link => link.SchoolId == school.Id && link.AppUserId != director.Id)
            .Select(link => link.AppUserId)
            .SingleAsync();
        var enrollment = Assert.Single(db.LevelEnrollments.Where(enrollment => enrollment.StudentId == studentId));
        Assert.Equal(klasse.Id, enrollment.KlasseId);
        Assert.False(enrollment.IsUnlocked);
        Assert.False(enrollment.IsPaid);
    }

    [Fact]
    public async Task CreatePlatformUserAsync_ProductOwnerDarfNurDirektorAnlegen()
    {
        using var db = TestDbContext.CreateInMemory();
        var productOwner = new AppUser { Id = Guid.NewGuid(), UserName = "owner", Email = "owner@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École Owner" };
        db.Users.Add(productOwner);
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = productOwner.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([productOwner]);
        userManager.SeedRole(productOwner, Rollen.ProduktOwner);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var directorResult = await service.CreatePlatformUserAsync(productOwner.Id, new UserFormModel
        {
            Vorname = "Dora",
            Nachname = "Directrice",
            Email = "dora.directrice@test.local",
            Password = "Start123!",
            RoleName = Rollen.SchulLeiter,
            SchoolIds = [school.Id]
        });

        var teacherResult = await service.CreatePlatformUserAsync(productOwner.Id, new UserFormModel
        {
            Vorname = "Theo",
            Nachname = "Teacher",
            Email = "theo.teacher@test.local",
            Password = "Start123!",
            RoleName = Rollen.Lehrer,
            SchoolIds = [school.Id]
        });

        Assert.True(directorResult.Succeeded, directorResult.Message);
        Assert.False(teacherResult.Succeeded);
        Assert.False((await service.SaveSchoolUserAsync(productOwner.Id, new UserFormModel
        {
            Vorname = "Zoé", Nachname = "Élève", Email = "zoe@test.local", RoleName = Rollen.Schueler,
            SchoolIds = [school.Id]
        })).Succeeded);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_KoordinatorDarfNurLehrerOderSchuelerAnlegen()
    {
        using var db = TestDbContext.CreateInMemory();
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "koord", Email = "koord@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École Koord" };
        db.Users.Add(coordinator);
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = coordinator.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([coordinator]);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var teacherResult = await service.SaveSchoolUserAsync(coordinator.Id, new UserFormModel
        {
            Vorname = "Lina",
            Nachname = "Lehr",
            Email = "lina.lehr@test.local",
            Password = "Start123!",
            RoleName = Rollen.Lehrer,
            SchoolIds = [school.Id]
        });

        var studentResult = await service.SaveSchoolUserAsync(coordinator.Id, new UserFormModel
        {
            Vorname = "Sami",
            Nachname = "Schueler",
            Email = "sami.schueler@test.local",
            Password = "Start123!",
            RoleName = Rollen.Schueler,
            SchoolIds = [school.Id]
        });

        var directorResult = await service.SaveSchoolUserAsync(coordinator.Id, new UserFormModel
        {
            Vorname = "Nora",
            Nachname = "Direktion",
            Email = "nora.direktion@test.local",
            Password = "Start123!",
            RoleName = Rollen.SchulLeiter,
            SchoolIds = [school.Id]
        });

        Assert.True(teacherResult.Succeeded, teacherResult.Message);
        Assert.True(studentResult.Succeeded, studentResult.Message);
        Assert.False(directorResult.Succeeded);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_DirektorDarfKeinenDirektorAnlegen()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        db.Users.Add(director);
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Ami",
            Nachname = "Directrice",
            Email = "ami.directrice@test.local",
            Password = "Start123!",
            RoleName = Rollen.SchulLeiter,
            SchoolIds = [school.Id]
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_KoordinatorErhaeltFinanzrechteInDerSchuleDerDirektion()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var schoolA = new School { Id = Guid.NewGuid(), Name = "École A" };
        var schoolB = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.Add(director);
        db.Schools.AddRange(schoolA, schoolB);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = schoolA.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());
        UserFormModel Koordinator(params Guid[] schulen) => new()
        {
            Vorname = "Kossi",
            Nachname = "Coord",
            Email = "kossi.coord@test.local",
            Password = "Start123!",
            RoleName = Rollen.Koordinator,
            SchoolIds = [.. schulen],
            CoordinatorFinanceAccesses = [.. schulen.Select(id => new CoordinatorFinanceAccessFormModel
                { SchoolId = id, Menu = true, Dashboard = true, Entries = true, Billing = true })]
        };

        // Ein Konto = eine Schule: keine zweite Schule für den Koordinator.
        Assert.False((await service.SaveSchoolUserAsync(director.Id, Koordinator(schoolA.Id, schoolB.Id))).Succeeded);

        var result = await service.SaveSchoolUserAsync(director.Id, Koordinator(schoolA.Id));
        Assert.True(result.Succeeded, result.Message);

        var coordinatorId = (await userManager.FindByEmailAsync("kossi.coord@test.local"))!.Id;
        var link = await db.UserSchools.SingleAsync(item => item.AppUserId == coordinatorId);
        Assert.Equal(schoolA.Id, link.SchoolId);
        Assert.True(link.CanManageStudents);
        Assert.True(link.CanAccessFinances);
        Assert.True(link.CanViewFinanceDashboard);
        Assert.True(link.CanCreateFinanceEntries);
        Assert.False(link.CanManageCyclePayments);
        Assert.True(link.CanAccessBilling);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_BearbeitetKoordinatorOhneBestehendeFinanzrechteZuVerlieren()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "coord@test.local", Email = "coord@test.local", Vorname = "Kossi", Nachname = "Kouma" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        db.Users.AddRange(director, coordinator);
        db.Schools.Add(school);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = coordinator.Id, SchoolId = school.Id, CanManageStudents = true,
                CanAccessFinances = true, CanCreateFinanceEntries = true, CanManageCyclePayments = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, coordinator]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var form = await service.GetSchoolUserForEditAsync(director.Id, coordinator.Id);
        Assert.NotNull(form);
        form.Vorname = "Kodjo";
        var result = await service.SaveSchoolUserAsync(director.Id, form);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Kodjo", coordinator.Vorname);
        var link = await db.UserSchools.SingleAsync(item => item.AppUserId == coordinator.Id);
        Assert.True(link.CanAccessFinances);
        Assert.False(link.CanViewFinanceDashboard);
        Assert.True(link.CanCreateFinanceEntries);
        Assert.True(link.CanManageCyclePayments);
        Assert.False(link.CanAccessBilling);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_EntziehtFinanzrechteDesKoordinators()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "koord", Email = "koord@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        db.Users.AddRange(director, coordinator);
        db.Schools.Add(school);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true },
            new UserSchool { AppUserId = coordinator.Id, SchoolId = school.Id, CanManageStudents = true,
                CanAccessFinances = true, CanCreateFinanceEntries = true, CanManageCyclePayments = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director, coordinator]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());
        var form = await service.GetSchoolUserForEditAsync(director.Id, coordinator.Id);
        Assert.NotNull(form);
        form.CoordinatorFinanceAccesses = [new CoordinatorFinanceAccessFormModel { SchoolId = school.Id }];

        var result = await service.SaveSchoolUserAsync(director.Id, form);

        Assert.True(result.Succeeded, result.Message);
        var link = await db.UserSchools.SingleAsync(item => item.AppUserId == coordinator.Id);
        Assert.False(link.CanAccessFinances);
        Assert.False(link.CanViewFinanceDashboard);
        Assert.False(link.CanCreateFinanceEntries);
        Assert.False(link.CanManageCyclePayments);
    }

    [Fact]
    public async Task SaveSchoolUserAsync_OhneFinanzOngletsWirdKeinFinanzzugriffVergeben()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var school = new School { Id = Guid.NewGuid(), Name = "École A" };
        db.Users.Add(director);
        db.Schools.Add(school);
        db.UserSchools.Add(new UserSchool { AppUserId = director.Id, SchoolId = school.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());
        var model = new UserFormModel
        {
            Vorname = "Kossi",
            Nachname = "Coord",
            Email = "kossi.coord@test.local",
            Password = "Start123!",
            RoleName = Rollen.Koordinator,
            SchoolIds = [school.Id],
            CoordinatorFinanceAccesses = [new CoordinatorFinanceAccessFormModel { SchoolId = school.Id, Menu = true }]
        };

        var withoutTab = await service.SaveSchoolUserAsync(director.Id, model);
        Assert.True(withoutTab.Succeeded, withoutTab.Message);
        var coordinatorId = (await userManager.FindByEmailAsync(model.Email))!.Id;
        var link = await db.UserSchools.SingleAsync(item => item.AppUserId == coordinatorId);
        Assert.False(link.CanAccessFinances);
        Assert.False(link.CanViewFinanceDashboard);
        Assert.False(link.CanCreateFinanceEntries);
        Assert.False(link.CanManageCyclePayments);

        model.Email = "autre.coord@test.local";
        model.CoordinatorFinanceAccesses =
        [
            new CoordinatorFinanceAccessFormModel { SchoolId = Guid.NewGuid(), Menu = true, Dashboard = true }
        ];
        var foreignSchool = await service.SaveSchoolUserAsync(director.Id, model);
        Assert.False(foreignSchool.Succeeded);
        Assert.DoesNotContain(await db.Users.ToListAsync(), user => user.Email == model.Email);
    }

    [Fact]
    public async Task GetSchoolLeaderSchoolsAsync_KoordinatorSiehtNurZugewieseneSchulen()
    {
        using var db = TestDbContext.CreateInMemory();
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "koord", Email = "koord@test.local" };
        var schoolA = new School { Id = Guid.NewGuid(), Name = "École A", City = "Lomé", Country = "Togo", IsActive = true };
        var schoolB = new School { Id = Guid.NewGuid(), Name = "École B", City = "Kara", Country = "Togo", IsActive = true };
        db.Users.Add(coordinator);
        db.Schools.AddRange(schoolA, schoolB);
        db.UserSchools.Add(new UserSchool { AppUserId = coordinator.Id, SchoolId = schoolA.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([coordinator]);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var schools = await service.GetSchoolLeaderSchoolsAsync(coordinator.Id);

        var school = Assert.Single(schools);
        Assert.Equal(schoolA.Id, school.SchoolId);
    }

    [Fact]
    public async Task GetSchoolKlassenAsync_KoordinatorSchuelerformularZeigtNurKlassenDerGewaehlenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var coordinator = new AppUser { Id = Guid.NewGuid(), UserName = "koord", Email = "koord@test.local" };
        var schoolA = new School { Id = Guid.NewGuid(), Name = "École A" };
        var schoolB = new School { Id = Guid.NewGuid(), Name = "École B" };
        var klasseA = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolA.Id, Code = "6e", Title = "Sixième" };
        var klasseB = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolB.Id, Code = "5e", Title = "Cinquième" };
        db.Users.Add(coordinator);
        db.Schools.AddRange(schoolA, schoolB);
        db.Klassen.AddRange(klasseA, klasseB);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = coordinator.Id, SchoolId = schoolA.Id, CanManageStudents = true },
            new UserSchool { AppUserId = coordinator.Id, SchoolId = schoolB.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([coordinator]);
        userManager.SeedRole(coordinator, Rollen.Koordinator);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var schools = await service.GetSchoolLeaderSchoolsAsync(coordinator.Id);
        Assert.Equal(2, schools.Count);
        var klassen = await service.GetSchoolKlassenAsync(schoolA.Id);
        Assert.Equal(klasseA.Id, Assert.Single(klassen).CourseId);
        Assert.DoesNotContain(klassen, klasse => klasse.CourseId == klasseB.Id);
    }

    [Fact]
    public async Task GetLessonForEditAsync_BehaeltFristBeimBearbeiten()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", Title = "Sixième" };
        var dueAtUtc = DateTime.UtcNow.AddDays(2);
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", Summary = "Résumé",
            Content = "Contenu", ExerciseSubmissionDueAtUtc = dueAtUtc
        };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());
        var form = await service.GetLessonForEditAsync(lesson.Id);

        Assert.NotNull(form);
        Assert.Equal(dueAtUtc, form.ExerciseSubmissionDueAtUtc);
        form.Title = "Leçon modifiée";
        var result = await service.SaveLessonAsync(form);

        Assert.True(result.Succeeded, result.Message);
        var saved = await db.Lessons.AsNoTracking().SingleAsync(x => x.Id == lesson.Id);
        Assert.Equal(dueAtUtc, saved.ExerciseSubmissionDueAtUtc);
    }

    [Fact]
    public async Task SaveLessonAsync_MitAbgabenVorFrist_SperrtBearbeitungInAktuellerRunde()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", Title = "Sixième" };
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            KlasseId = klasse.Id,
            Title = "Leçon 1",
            Summary = "Résumé",
            Content = "Contenu",
            Order = 1,
            ExerciseSubmissionDueAtUtc = DateTime.UtcNow.AddDays(2),
            AssignmentVersion = 1
        };
        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            Title = "Exercice",
            Question = "Question",
            ExerciseType = "SingleChoice",
            Order = 1
        };
        var studentId = Guid.NewGuid();

        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Lessons.Add(lesson);
        db.Exercises.Add(exercise);
        db.ExerciseAttempts.Add(new ExerciseAttempt
        {
            Id = Guid.NewGuid(),
            ExerciseId = exercise.Id,
            StudentId = studentId,
            AssignmentVersion = 1,
            IsCorrect = true,
            AnsweredAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());

        var result = await service.SaveLessonAsync(new LessonFormModel
        {
            LessonId = lesson.Id,
            CourseId = klasse.Id,
            Title = "Leçon modifiée",
            Summary = "Résumé",
            Content = "Contenu",
            IsPublished = true,
            ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc,
            Exercises =
            [
                new LessonExerciseFormModel
                {
                    ExerciseId = exercise.Id,
                    Title = "Exercice",
                    Question = "Question",
                    ExerciseType = "SingleChoice",
                    Order = 1,
                    Points = 10,
                    Options = [ new LessonExerciseOptionFormModel { Text = "A", IsCorrect = true } ]
                }
            ]
        });

        Assert.False(result.Succeeded);
        var persisted = await db.Lessons.AsNoTracking().SingleAsync(x => x.Id == lesson.Id);
        Assert.Equal(1, persisted.AssignmentVersion);
        Assert.Equal("Leçon 1", persisted.Title);
    }

    [Fact]
    public async Task SaveLessonAsync_MitAbgabenNachFrist_ErhohtVersionUndSpeichertAenderung()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", Title = "Sixième" };
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            KlasseId = klasse.Id,
            Title = "Leçon 1",
            Summary = "Résumé",
            Content = "Contenu",
            Order = 1,
            ExerciseSubmissionDueAtUtc = DateTime.UtcNow.AddDays(-1),
            AssignmentVersion = 1
        };
        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            Title = "Exercice",
            Question = "Question",
            ExerciseType = "SingleChoice",
            Order = 1
        };
        var studentId = Guid.NewGuid();

        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Lessons.Add(lesson);
        db.Exercises.Add(exercise);
        db.ExerciseAttempts.Add(new ExerciseAttempt
        {
            Id = Guid.NewGuid(),
            ExerciseId = exercise.Id,
            StudentId = studentId,
            AssignmentVersion = 1,
            IsCorrect = true,
            AnsweredAtUtc = DateTime.UtcNow.AddHours(-2)
        });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());

        var result = await service.SaveLessonAsync(new LessonFormModel
        {
            LessonId = lesson.Id,
            CourseId = klasse.Id,
            Title = "Leçon modifiée",
            Summary = "Résumé 2",
            Content = "Contenu 2",
            IsPublished = true,
            ExerciseSubmissionDueAtUtc = DateTime.UtcNow.AddDays(3),
            Exercises =
            [
                new LessonExerciseFormModel
                {
                    ExerciseId = exercise.Id,
                    Title = "Exercice",
                    Question = "Question",
                    ExerciseType = "SingleChoice",
                    Order = 1,
                    Points = 10,
                    Options = [ new LessonExerciseOptionFormModel { Text = "A", IsCorrect = true } ]
                }
            ]
        });

        Assert.True(result.Succeeded, result.Message);
        var persisted = await db.Lessons.AsNoTracking().SingleAsync(x => x.Id == lesson.Id);
        Assert.Equal(2, persisted.AssignmentVersion);
        Assert.Equal("Leçon modifiée", persisted.Title);
    }

    [Fact]
    public async Task ChangeLessonSubmissionDueAsync_VerlaengerungOeffnetNeueAbgabeversionFuerAlle()
    {
        using var db = TestDbContext.CreateInMemory();
        var teacherId = Guid.NewGuid();
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", Title = "Sixième" };
        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", Summary = "Résumé", Content = "Contenu", CreatedByTeacherId = teacherId, AssignmentVersion = 1, ExerciseSubmissionDueAtUtc = DateTime.UtcNow.AddHours(-1), IsPublished = true };
        var exercise = new Exercise { Id = Guid.NewGuid(), LessonId = lesson.Id, Title = "Exercice", Question = "Question", ExerciseType = "SingleChoice", Order = 1 };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Lessons.Add(lesson);
        db.Exercises.Add(exercise);
        db.ExerciseAttempts.Add(new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = exercise.Id, StudentId = Guid.NewGuid(), AssignmentVersion = 1, IsCorrect = true });
        await db.SaveChangesAsync();
        var service = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());
        var newDueAtUtc = DateTime.UtcNow.AddDays(1);

        var result = await service.ChangeLessonSubmissionDueAsync(lesson.Id, newDueAtUtc, teacherId);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, lesson.AssignmentVersion);
        Assert.Equal(newDueAtUtc, lesson.ExerciseSubmissionDueAtUtc);
        Assert.Equal(1, db.ExerciseAttempts.Single().AssignmentVersion);
        await service.ChangeLessonSubmissionDueAsync(lesson.Id, newDueAtUtc, teacherId);
        Assert.Equal(2, lesson.AssignmentVersion);
    }

    [Fact]
    public async Task ChangeLessonSubmissionDueAsync_FremderLehrerDarfNichtVerlaengern()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Lomé" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", Title = "Sixième" };
        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", CreatedByTeacherId = Guid.NewGuid(), AssignmentVersion = 1 };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Lessons.Add(lesson);
        await db.SaveChangesAsync();

        var result = await new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService())
            .ChangeLessonSubmissionDueAsync(lesson.Id, DateTime.UtcNow.AddDays(1), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Null(lesson.ExerciseSubmissionDueAtUtc);
    }
}
