using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SchuelerZugangServiceTests
{
    [Fact]
    public async Task MaterialOhneAboVorUndNachZyklusabschlussBleibtZugaenglich()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", SubscriptionPriceOverride = 3000 };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève", IsSelfRegistered = true };
        var profil = new StudentProfile { AppUserId = student.Id, SchoolId = schule.Id, CurrentKlasseId = klasse.Id, AufnahmeBestaetigtAmUtc = DateTime.UtcNow };
        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", IsPublished = true };
        db.Schools.Add(schule); db.Klassen.Add(klasse); db.Users.Add(student); db.StudentProfiles.Add(profil);
        db.Lessons.AddRange(lesson, new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Brouillon", IsPublished = false });
        await db.SaveChangesAsync();
        var learning = new LearningService(db);
        Assert.Empty(await db.LevelEnrollments.ToListAsync());
        Assert.Equal(lesson.Id, Assert.Single(await learning.GetLessonsForStudentAsync(student.Id)).LessonId);
        Assert.NotNull(await learning.GetLessonDetailAsync(lesson.Id, student.Id));
        var finanz = new FinanzService(db);
        var start = await finanz.DurchlaufStartenAsync(schule.Id, klasse.Id, 1000, DateTime.Today.AddDays(-1), student.Id);
        Assert.True(start.Succeeded, start.ErrorMessage);
        var zyklus = await db.KursNiveauDurchlaeufe.SingleAsync();
        Assert.True((await finanz.DurchlaufBeendenAsync(zyklus.Id, DateTime.Today, student.Id)).Succeeded);
        Assert.False((await db.LevelEnrollments.SingleAsync()).IsPaid);
        Assert.Equal(lesson.Id, Assert.Single(await learning.GetLessonsForStudentAsync(student.Id)).LessonId);
        Assert.NotNull(await learning.GetLessonDetailAsync(lesson.Id, student.Id));
        var dashboard = await new DashboardService(db, TestUserManager.Create([student]), new FakeMediaStorageService()).GetStudentDashboardAsync(student.Id);
        Assert.NotNull(dashboard); Assert.True(dashboard.IsCurrentClassUnlocked); Assert.False(dashboard.IsCurrentClassPaid);
        student.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Empty(await learning.GetLessonsForStudentAsync(student.Id));
        Assert.Null(await learning.GetLessonDetailAsync(lesson.Id, student.Id));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task GesperrteArchivierteOderInaktiveSchuleErteilenKeinenZugang(bool blocked, bool archived, bool inactive)
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", IsActive = !inactive };
        var student = new AppUser { Id = Guid.NewGuid(), IsBlocked = blocked };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e" };
        db.Schools.Add(schule); db.Users.Add(student); db.Klassen.Add(klasse);
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = schule.Id, CurrentKlasseId = klasse.Id, IsArchived = archived });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = klasse.Id, IsUnlocked = true, IsPaid = true });
        await db.SaveChangesAsync();
        Assert.Empty(await new SchuelerZugangService(db).KlassenAsync(student.Id, true));
    }

    [Fact]
    public async Task FremdeKlassenWerdenAuchBeiInkonsistenterZuordnungNichtFreigegeben()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École" };
        var fremd = new School { Id = Guid.NewGuid(), Name = "Autre école" };
        var student = new AppUser { Id = Guid.NewGuid() };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = fremd.Id, Code = "6e" };
        db.Schools.AddRange(schule, fremd); db.Users.Add(student); db.Klassen.Add(klasse);
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = schule.Id, CurrentKlasseId = klasse.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = klasse.Id, IsUnlocked = true });
        await db.SaveChangesAsync();
        Assert.Empty(await new SchuelerZugangService(db).KlassenAsync(student.Id, true));
    }
}
