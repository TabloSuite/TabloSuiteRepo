using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SchulSperreInhaltsTests
{
    [Fact]
    public async Task LektionsAufgabenLesepfadPrueftRolleZielschuleUndPersoenlicheSperre()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "A", IsActive = true };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CM1" };
        var director = new AppUser { Id = Guid.NewGuid() };
        var student = new AppUser { Id = Guid.NewGuid() };
        var role = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Leçon", IsPublished = true };
        db.Schools.Add(school); db.Klassen.Add(klasse); db.Users.AddRange(director, student); db.Roles.Add(role);
        db.UserRoles.Add(new AppUserRole { UserId = director.Id, RoleId = role.Id });
        db.UserSchools.AddRange(new UserSchool { AppUserId = director.Id, SchoolId = school.Id },
            new UserSchool { AppUserId = student.Id, SchoolId = school.Id, CanManageStudents = true });
        db.Lessons.Add(lesson); await db.SaveChangesAsync();
        var service = new LearningService(db);
        Assert.NotNull(await service.GetLessonDetailForTeacherOrDirectionReadOnlyAsync(lesson.Id, director.Id));
        Assert.Null(await service.GetLessonDetailForTeacherOrDirectionReadOnlyAsync(lesson.Id, student.Id));
        director.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Null(await service.GetLessonDetailForTeacherOrDirectionReadOnlyAsync(lesson.Id, director.Id));
        director.IsBlocked = false; school.IsActive = false; await db.SaveChangesAsync();
        Assert.Null(await service.GetLessonDetailForTeacherOrDirectionReadOnlyAsync(lesson.Id, director.Id));
    }

    [Fact]
    public async Task AktiveZweitschuleErlaubtKeinePruefungsUndLektionsAktionenInGesperrterSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var a = new School { Id = Guid.NewGuid(), Name = "A" };
        var b = new School { Id = Guid.NewGuid(), Name = "B" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = a.Id, Code = "CM1" };
        var direktor = new AppUser { Id = Guid.NewGuid(), IsBlocked = true };
        var lehrer = new AppUser { Id = Guid.NewGuid() };
        var role = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        db.Schools.AddRange(a, b); db.Klassen.Add(klasse); db.Users.AddRange(direktor, lehrer); db.Roles.Add(role);
        db.UserRoles.Add(new AppUserRole { UserId = direktor.Id, RoleId = role.Id });
        db.UserSchools.AddRange(new UserSchool { AppUserId = direktor.Id, SchoolId = a.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = a.Id }, new UserSchool { AppUserId = lehrer.Id, SchoolId = b.Id });
        var exam = new Exam { Id = Guid.NewGuid(), KlasseId = klasse.Id, AuthorId = lehrer.Id, Title = "Test", IsPublishedToStudents = true, SubmissionDueAtUtc = DateTime.UtcNow.AddHours(1) };
        var lesson = new TeacherLesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, AuthorId = lehrer.Id, Title = "Support" };
        db.Exams.Add(exam); db.TeacherLessons.Add(lesson); await db.SaveChangesAsync();
        Assert.True(await new SchulZugangService(db).DarfAnmeldenAsync(lehrer.Id));
        var exams = new ExamService(db); var lessons = new TeacherLessonService(db);
        Assert.Empty(await exams.GetTeacherExamsAsync(lehrer.Id));
        Assert.Null(await exams.GetExamForEditAsync(lehrer.Id, exam.Id));
        Assert.Null(await exams.GetExamForPdfAsync(lehrer.Id, exam.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => exams.ChangeSubmissionDueAsync(lehrer.Id, exam.Id, DateTime.UtcNow.AddHours(2)));
        Assert.False((await exams.DeleteExamAsync(lehrer.Id, exam.Id)).Succeeded);
        Assert.Empty(await lessons.GetLessonsForTeacherAsync(lehrer.Id));
        Assert.Null(await lessons.GetLessonForEditAsync(lesson.Id, lehrer.Id));
        Assert.Empty(await lessons.GetLessonsForTeacherOrSchoolDirectionAsync(lehrer.Id, true));
        Assert.Null(await lessons.GetLessonDetailForPdfExportAsync(lesson.Id, lehrer.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lessons.SetPublishedAsync(lesson.Id, lehrer.Id, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lessons.DeleteLessonAsync(lesson.Id, lehrer.Id));
        Assert.False(lesson.IsPublished); Assert.False(lehrer.IsBlocked);
        direktor.IsBlocked = false; await db.SaveChangesAsync();
        Assert.Single(await exams.GetTeacherExamsAsync(lehrer.Id)); Assert.Single(await lessons.GetLessonsForTeacherAsync(lehrer.Id));
    }
}
