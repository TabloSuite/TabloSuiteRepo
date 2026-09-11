using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="DashboardService"/> auf Basis eines In-Memory-Kontexts
/// und eines Test-UserManagers.
/// </summary>
public class DashboardServiceTests
{
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
    public async Task SaveUserAsync_SchulLeiterErhaeltMehrereVomSuperAdminZugewieseneSchulen()
    {
        using var db = TestDbContext.CreateInMemory();
        var firstSchool = new School { Id = Guid.NewGuid(), Name = "École A", City = "Lomé" };
        var secondSchool = new School { Id = Guid.NewGuid(), Name = "École B", City = "Kara" };
        db.Schools.AddRange(firstSchool, secondSchool);
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([]);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveUserAsync(new UserFormModel
        {
            Vorname = "Marie",
            Nachname = "Directrice",
            Email = "marie.directrice@test.local",
            RoleName = "SchulLeiter",
            Password = "Start123!",
            SchoolIds = [firstSchool.Id, secondSchool.Id]
        });

        Assert.True(result.Succeeded, result.Message);
        var links = await db.UserSchools
            .Where(link => link.CanManageStudents)
            .ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.Single(links.Select(link => link.AppUserId).Distinct());
        Assert.Equal(
            new[] { firstSchool.Id, secondSchool.Id }.OrderBy(id => id),
            links.Select(link => link.SchoolId).OrderBy(id => id));
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
            new StudentProfile { AppUserId = firstStudent.Id, SchoolId = firstSchool.Id, CurrentLevelCode = "A1.1" },
            new StudentProfile { AppUserId = secondStudent.Id, SchoolId = secondSchool.Id, CurrentLevelCode = "A2.1" },
            new StudentProfile { AppUserId = foreignStudent.Id, SchoolId = foreignSchool.Id, CurrentLevelCode = "B1.1" });
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
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentLevelCode = "A1.1" });
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
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentLevelCode = "A1.1" });
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
        db.Klassen.Add(new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Anfänger" });
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
        var klasseA1 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Débutant" };
        var klasseA2 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "Élémentaire" };
        db.Klassen.AddRange(klasseA1, klasseA2);

        var schuelerA = new AppUser { Id = Guid.NewGuid(), FullName = "Élève A", UserName = "eleveA" };
        var schuelerB = new AppUser { Id = Guid.NewGuid(), FullName = "Élève B", UserName = "eleveB" };
        db.Users.AddRange(schuelerA, schuelerB);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerA.Id, SchoolId = schuleId, CurrentLevelCode = "A1.1", CurrentKlasseId = klasseA1.Id });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerB.Id, SchoolId = schuleId, CurrentLevelCode = "A2.1", CurrentKlasseId = klasseA2.Id });

        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = teacherId, SchulID = schuleId, KlasseId = klasseA1.Id });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schuelerA, schuelerB });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var progress = await service.GetTeacherStudentProgressAsync(teacherId);

        // Assert
        var eintrag = Assert.Single(progress);
        Assert.Equal("Élève A", eintrag.FullName);
        Assert.Equal("A1.1", eintrag.CurrentLevelCode);
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
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerA.Id, SchoolId = schuleId, CurrentLevelCode = "A1.1" });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerB.Id, SchoolId = schuleId, CurrentLevelCode = "A2.1" });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schuelerA, schuelerB });
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var progress = await service.GetTeacherStudentProgressAsync(teacherId);

        // Assert
        Assert.Empty(progress);
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
        var levelA1 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Débutant" };
        db.Klassen.Add(levelA1);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schueler.Id, SchoolId = schuleId, CurrentLevelCode = "A1.1", CurrentKlasseId = levelA1.Id });
        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = teacherId, SchulID = schuleId, KlasseId = levelA1.Id });

        // Übungsversuche: eine richtig (100), eine falsch (0). Ein offener manueller Versuch zählt nicht.
        db.ExerciseAttempts.AddRange(
            new ExerciseAttempt { ExerciseId = Guid.NewGuid(), StudentId = schueler.Id, IsCorrect = true, RequiresManualGrading = false },
            new ExerciseAttempt { ExerciseId = Guid.NewGuid(), StudentId = schueler.Id, IsCorrect = false, RequiresManualGrading = false },
            new ExerciseAttempt { ExerciseId = Guid.NewGuid(), StudentId = schueler.Id, IsCorrect = false, RequiresManualGrading = true });

        // Prüfungsversuch: abgeschlossen, automatisch bewertet mit 80 %. Ein nicht abgeschlossener zählt nicht.
        db.ExamAttempts.AddRange(
            new ExamAttempt { ExamId = Guid.NewGuid(), StudentId = schueler.Id, IsCompleted = true, RequiresManualGrading = false, ScorePercent = 80 },
            new ExamAttempt { ExamId = Guid.NewGuid(), StudentId = schueler.Id, IsCompleted = false, RequiresManualGrading = false, ScorePercent = 20 });
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
        var result = await service.SaveSchoolAsync(form, new List<Guid>());

        // Assert
        Assert.True(result.Succeeded);
        var school = Assert.Single(db.Schools);
        Assert.Equal(15000m, school.PreisHalloBasis);
        Assert.Equal(15000m, school.PreisHalloPlus);
        Assert.Equal(15000m, school.EffektiverPreis(AboTyp.HalloBasis));
        Assert.Equal(15000m, school.EffektiverPreis(AboTyp.HalloPlus));
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
            PreisHalloBasis = null,
            PreisHalloPlus = null
        });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create();
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        // Act
        var form = await service.GetSchoolForEditAsync(schuleId);

        // Assert
        Assert.NotNull(form);
        Assert.Null(form!.PreisHalloBasis);
        Assert.Null(form.PreisHalloPlus);
    }

    [Fact]
    public async Task SaveSchoolAsync_PersistiertSchulSpezifischeCommissionOverrides()
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

        var result = await service.SaveSchoolAsync(form, []);

        Assert.True(result.Succeeded, result.Message);
        var school = Assert.Single(db.Schools);
        Assert.Equal(6m, school.SchoolLeaderCommissionPercentOverride);
        Assert.Equal(8m, school.TeacherCommissionPercentOverride);
        Assert.Equal(2m, school.ProductOwnerCommissionPercentOverride);
    }

    [Fact]
    public async Task SaveSchoolAsync_LehntCommissionOverridesUeberHundertProzentAb()
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

        var result = await service.SaveSchoolAsync(form, []);

        Assert.False(result.Succeeded);
        Assert.Contains("100", result.Message);
        Assert.Empty(db.Schools);
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
    public async Task SetStudentBlockedStateAsync_BezahltesNiveau_SchulLeiterKannNichtBlockieren()
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

        // Assert – bleibt unblockiert
        var aktualisiert = Assert.Single(db.Users);
        Assert.False(aktualisiert.IsBlocked);
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
            Code = "A1.1",
            Title = "Découvrir l'allemand",
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
            Code = "A1.1",
            Title = "Geändert",
            CEFRGroup = "A",
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
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Découvrir l’Allemand", SchoolId = schule.Id };
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
    public async Task SaveSchoolKlasseAsync_Sprachschule_LehntDoppelteKlassenNamenAb()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "Sprachschule", IsActive = true };
        db.Schools.Add(schule);
        // Ein globales System-Niveau darf nicht mitzählen.
        db.Klassen.Add(new Klasse { Id = Guid.NewGuid(), Code = "A1.1", IstSystemNiveau = true, SchoolId = null });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, TestUserManager.Create(Array.Empty<AppUser>()), new FakeMediaStorageService());

        // Act
        var ersteKlasse = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Code = "EXAM", Title = "Préparation examen", ReferenceName = "Préparation examen", NeueKlassenStufe = "Préparation examen", CEFRGroup = "B", Description = "x"
        });
        var zweiteKlasse = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Code = "EXAM2", Title = "Préparation examen", ReferenceName = "Préparation examen", NeueKlassenStufe = "Préparation examen", CEFRGroup = "B", Description = "x"
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
            Code = "6A", Title = "Sixième A", Description = "Classe", CEFRGroup = "Hors CECR", NeueKlassenStufe = "6e"
        });

        Assert.False(result.Succeeded);
        Assert.False(await db.Klassen.AnyAsync(klasse => klasse.SchoolId == fremdeSchule.Id && klasse.Code == "6A"));
    }

    [Fact]
    public async Task GetSchoolKlassenAsync_SchliesstGlobaleSystemNiveausAus()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "Sprachschule", IsActive = true };
        db.Schools.Add(schule);
        db.Klassen.AddRange(
            new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Standard", IstSystemNiveau = true, SchoolId = null },
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
            Title = "CEP1A", CEFRGroup = "-", NeueKlassenStufe = "CEP1", Description = "x"
        });
        var klasse2 = await service.SaveSchoolKlasseAsync(schule.Id, new CourseFormModel
        {
            Title = "CEP1B", CEFRGroup = "-", NeueKlassenStufe = "CEP1", Description = "x"
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
        var systemNiveau = new Klasse { Id = Guid.NewGuid(), Code = "B1.1", IstSystemNiveau = true, SchoolId = null };
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
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id, IstSystemNiveau = false };
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
    public async Task SaveSchoolUserAsync_ProductOwnerDarfNurDirektorAnlegen()
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

        var directorResult = await service.SaveSchoolUserAsync(productOwner.Id, new UserFormModel
        {
            Vorname = "Dora",
            Nachname = "Directrice",
            Email = "dora.directrice@test.local",
            Password = "Start123!",
            RoleName = Rollen.SchulLeiter,
            SchoolIds = [school.Id]
        });

        var teacherResult = await service.SaveSchoolUserAsync(productOwner.Id, new UserFormModel
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
    public async Task SaveSchoolUserAsync_DirektorKannKoordinatorMehrerenEigenenSchulenZuweisen()
    {
        using var db = TestDbContext.CreateInMemory();
        var director = new AppUser { Id = Guid.NewGuid(), UserName = "leiter", Email = "leiter@test.local" };
        var schoolA = new School { Id = Guid.NewGuid(), Name = "École A" };
        var schoolB = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Users.Add(director);
        db.Schools.AddRange(schoolA, schoolB);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = director.Id, SchoolId = schoolA.Id, CanManageStudents = true },
            new UserSchool { AppUserId = director.Id, SchoolId = schoolB.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var service = new DashboardService(db, userManager, new FakeMediaStorageService());

        var result = await service.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Kossi",
            Nachname = "Coord",
            Email = "kossi.coord@test.local",
            Password = "Start123!",
            RoleName = Rollen.Koordinator,
            SchoolIds = [schoolA.Id, schoolB.Id]
        });

        Assert.True(result.Succeeded, result.Message);

        var coordinatorId = await db.UserSchools
            .Where(link => (link.SchoolId == schoolA.Id || link.SchoolId == schoolB.Id) && link.AppUserId != director.Id)
            .Select(link => link.AppUserId)
            .Distinct()
            .SingleAsync();

        var assignedSchoolIds = await db.UserSchools
            .Where(link => link.AppUserId == coordinatorId)
            .Select(link => link.SchoolId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(2, assignedSchoolIds.Count);
        Assert.Contains(schoolA.Id, assignedSchoolIds);
        Assert.Contains(schoolB.Id, assignedSchoolIds);
        Assert.All(await db.UserSchools.Where(link => link.AppUserId == coordinatorId).ToListAsync(), link => Assert.True(link.CanManageStudents));
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
            Order = 1,
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
            Order = 2,
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

}