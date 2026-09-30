using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="TeacherLessonService"/>: Speichern mit Seiten,
/// Sichtbarkeit für Schüler nach aktuellem Niveau und Veröffentlichung.
/// </summary>
public class TeacherLessonServiceTests
{
    [Fact]
    public async Task SaveLessonAsync_ErstelltLektionMitSeiten()
    {
        using var db = TestDbContext.CreateInMemory();
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1" };
        db.Klassen.Add(level);
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var authorId = Guid.NewGuid();

        var model = new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Grammatik",
            Summary = "Einführung",
            IsPublished = true,
            Pages =
            [
                new TeacherLessonPageFormModel { ContentHtml = "<p>Seite 1</p>" },
                new TeacherLessonPageFormModel { ContentHtml = "<p>Seite 2</p>" }
            ]
        };

        var id = await service.SaveLessonAsync(model, authorId);

        var detail = await service.GetLessonDetailAsync(id, onlyPublished: false);
        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Pages.Count);
        Assert.Equal(1, detail.Pages[0].PageNumber);
        Assert.Equal(2, detail.Pages[1].PageNumber);
        Assert.Equal("<p>Seite 1</p>", detail.Pages[0].ContentHtml);
    }

    [Fact]
    public async Task SaveLessonAsync_Systemniveau_WirdAbgewiesen()
    {
        using var db = TestDbContext.CreateInMemory();
        var systemLevel = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", IstSystemNiveau = true };
        db.Klassen.Add(systemLevel);
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = systemLevel.Id,
            Title = "Nicht erlaubt",
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>Inhalt</p>" }]
        }, Guid.NewGuid()));

        Assert.Contains("niveaux standard", exception.Message);
    }

    [Fact]
    public async Task SaveLessonAsync_SpeichertEingebettetesBild()
    {
        using var db = TestDbContext.CreateInMemory();
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1" };
        db.Klassen.Add(level);
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        const string imageHtml = "<p>Illustration</p><img src=\"data:image/png;base64,iVBORw0KGgo=\" alt=\"Exemple\" />";

        var id = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Cours avec image",
            Pages = [new TeacherLessonPageFormModel { ContentHtml = imageHtml }]
        }, Guid.NewGuid());

        var detail = await service.GetLessonDetailAsync(id, onlyPublished: false);

        Assert.NotNull(detail);
        Assert.Equal(imageHtml, detail!.Pages[0].ContentHtml);
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_ZeigtNurVeroeffentlichteFuerAktuellesNiveau()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var levelA1 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1" };
        var levelA2 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2" };
        db.Schools.Add(new School { Id = schoolId, Name = "École", City = "Yaoundé" });
        db.Users.Add(new AppUser { Id = authorId, FullName = "Enseignant", UserName = "enseignant" });
        db.Klassen.AddRange(levelA1, levelA2);
        db.UserSchools.Add(new UserSchool { AppUserId = authorId, SchoolId = schoolId, CanManageStudents = true });

        var studentId = Guid.NewGuid();
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = schoolId,
            CurrentLevelCode = "A1.1",
            CurrentKlasseId = levelA1.Id
        });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = levelA1.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);

        // Veröffentlicht, richtiges Niveau -> sichtbar
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = levelA1.Id,
            Title = "A1 veröffentlicht",
            IsPublished = true,
            IsSharedWithSchool = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, authorId);

        // Entwurf, richtiges Niveau -> nicht sichtbar
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = levelA1.Id,
            Title = "A1 Entwurf",
            IsPublished = false,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, authorId);

        // Veröffentlicht, anderes Niveau -> nicht sichtbar
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = levelA2.Id,
            Title = "A2 veröffentlicht",
            IsPublished = true,
            IsSharedWithSchool = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, authorId);

        var lessons = await service.GetLessonsForStudentAsync(studentId);

        Assert.Single(lessons);
        Assert.Equal("A1 veröffentlicht", lessons[0].Title);
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_ZeigtVeroeffentlichteMaterialienAusFrueherenKlassen()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var levelA1 = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "A1.1", Title = "A1" };
        var levelB2 = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "B2.1", Title = "B2" };
        db.Schools.Add(new School { Id = schoolId, Name = "École", City = "Yaoundé" });
        db.Users.Add(new AppUser { Id = authorId, FullName = "Enseignant", UserName = "enseignant" });
        db.Klassen.AddRange(levelA1, levelB2);
        db.UserSchools.Add(new UserSchool { AppUserId = authorId, SchoolId = schoolId, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = schoolId,
            CurrentKlasseId = levelB2.Id,
            CurrentLevelCode = "B2.1"
        });
        db.LevelEnrollments.AddRange(
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = levelA1.Id, IsUnlocked = true },
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = levelB2.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = levelA1.Id,
            Title = "Support A1",
            IsPublished = true,
            IsSharedWithSchool = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>A1</p>" }]
        }, authorId);
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = levelB2.Id,
            Title = "Support B2",
            IsPublished = true,
            IsSharedWithSchool = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>B2</p>" }]
        }, authorId);

        var lessons = await service.GetLessonsForStudentAsync(studentId);

        Assert.Equal(["Support A1", "Support B2"], lessons.Select(lesson => lesson.Title));
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_ZeigtVeroeffentlichteLektionAuchOhneDirektionsFreigabe()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1" };
        db.Schools.Add(new School { Id = schoolId, Name = "École" });
        db.Users.Add(new AppUser { Id = authorId, FullName = "Enseignant", UserName = "enseignant" });
        db.Klassen.Add(level);
        db.UserSchools.Add(new UserSchool { AppUserId = authorId, SchoolId = schoolId, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentKlasseId = level.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = level.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Support pour élèves",
            IsPublished = true,
            IsSharedWithSchool = false,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, authorId);

        var lessons = await service.GetLessonsForStudentAsync(studentId);

        Assert.Single(lessons);
        Assert.Equal("Support pour élèves", lessons[0].Title);
    }

    [Fact]
    public async Task GetLessonsForTeacherOrSchoolDirectionAsync_ZeigtDirektionGeteilteLektionDerselbenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var directionId = Guid.NewGuid();
        var level = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "A1.1", Title = "A1" };
        db.Schools.Add(new School { Id = schoolId, Name = "École" });
        db.Users.AddRange(
            new AppUser { Id = teacherId, FullName = "Enseignant", UserName = "enseignant" },
            new AppUser { Id = directionId, FullName = "Direction", UserName = "direction" });
        db.Klassen.Add(level);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = teacherId, SchoolId = schoolId, CanManageStudents = true },
            new UserSchool { AppUserId = directionId, SchoolId = schoolId, CanManageStudents = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Support partagé direction",
            IsPublished = true,
            IsSharedWithSchool = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, teacherId);

        var lessons = await service.GetLessonsForTeacherOrSchoolDirectionAsync(directionId, includeSharedWithDirection: true);

        var lesson = Assert.Single(lessons);
        Assert.Equal("Support partagé direction", lesson.Title);
        Assert.Equal(teacherId, lesson.AuthorId);
        Assert.True(lesson.IsSharedWithSchool);
    }

    [Fact]
    public async Task GetLessonsForTeacherOrSchoolDirectionAsync_OhneDirektionRecht_ZeigtGeteilteFremdLektionNicht()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var productOwnerId = Guid.NewGuid();
        var level = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "A1.1", Title = "A1" };
        db.Schools.Add(new School { Id = schoolId, Name = "École" });
        db.Users.AddRange(
            new AppUser { Id = teacherId, FullName = "Enseignant", UserName = "enseignant" },
            new AppUser { Id = productOwnerId, FullName = "Owner", UserName = "owner" });
        db.Klassen.Add(level);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = teacherId, SchoolId = schoolId, CanManageStudents = true },
            new UserSchool { AppUserId = productOwnerId, SchoolId = schoolId, CanManageStudents = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Support partagé direction",
            IsPublished = true,
            IsSharedWithSchool = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, teacherId);

        var lessons = await service.GetLessonsForTeacherOrSchoolDirectionAsync(productOwnerId, includeSharedWithDirection: false);

        Assert.Empty(lessons);
    }

    [Fact]
    public async Task GetLessonDetailForDirectionAsync_LiefertGeteilteLektionSchreibgeschuetzt()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var directionId = Guid.NewGuid();
        var level = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "A1.1", Title = "A1" };
        db.Schools.Add(new School { Id = schoolId, Name = "École" });
        db.Users.AddRange(
            new AppUser { Id = teacherId, FullName = "Enseignant", UserName = "enseignant" },
            new AppUser { Id = directionId, FullName = "Direction", UserName = "direction" });
        db.Klassen.Add(level);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = teacherId, SchoolId = schoolId, CanManageStudents = true },
            new UserSchool { AppUserId = directionId, SchoolId = schoolId, CanManageStudents = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var lessonId = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Support partagé",
            IsSharedWithSchool = true,
            IsPdfExportEnabled = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, teacherId);
        var detail = await service.GetLessonDetailForDirectionAsync(lessonId, directionId);

        Assert.NotNull(detail);
        Assert.Equal("Support partagé", detail!.Title);
        Assert.True(detail.IsPdfExportEnabled);
        Assert.Single(detail.Pages);
    }

    [Fact]
    public async Task GetLessonDetailForPdfExportAsync_ErlaubtDirektionBeiFreigabe()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var teacherId = Guid.NewGuid();
        var directionId = Guid.NewGuid();
        var level = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "A1.1", Title = "A1" };
        db.Schools.Add(new School { Id = schoolId, Name = "École" });
        db.Users.AddRange(
            new AppUser { Id = teacherId, FullName = "Enseignant", UserName = "enseignant" },
            new AppUser { Id = directionId, FullName = "Direction", UserName = "direction" });
        db.Klassen.Add(level);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = teacherId, SchoolId = schoolId, CanManageStudents = true },
            new UserSchool { AppUserId = directionId, SchoolId = schoolId, CanManageStudents = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var lessonId = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "Support exportable",
            IsSharedWithSchool = true,
            IsPdfExportEnabled = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, teacherId);
        var detail = await service.GetLessonDetailForPdfExportAsync(
            lessonId, directionId, includeSharedWithDirection: true);

        Assert.NotNull(detail);
        Assert.Equal("Support exportable", detail!.Title);
        Assert.Single(detail.Pages);
    }

    [Fact]
    public async Task GetLessonDetailForStudentAsync_VerweigertAnderesNiveau()
    {
        using var db = TestDbContext.CreateInMemory();
        var levelA2 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2" };
        db.Klassen.Add(levelA2);

        var studentId = Guid.NewGuid();
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = Guid.NewGuid(),
            CurrentLevelCode = "A1.1"
        });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var id = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = levelA2.Id,
            Title = "A2",
            IsPublished = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, Guid.NewGuid());

        var detail = await service.GetLessonDetailForStudentAsync(id, studentId);

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetLessonDetailForStudentPdfExportAsync_ErfordertSchulFreigabe()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1" };
        var authorId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        db.Schools.Add(new School { Id = schoolId, Name = "École" });
        db.Klassen.Add(klasse);
        db.Users.Add(new AppUser { Id = authorId, FullName = "Enseignant", UserName = "enseignant" });
        db.UserSchools.Add(new UserSchool { AppUserId = authorId, SchoolId = schoolId, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentKlasseId = klasse.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = klasse.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var lessonId = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = klasse.Id,
            Title = "Support exportable",
            IsPublished = true,
            IsSharedWithSchool = true,
            IsPdfExportEnabled = false,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>Contenu</p>" }]
        }, authorId);

        Assert.Null(await service.GetLessonDetailForStudentPdfExportAsync(lessonId, studentId));

        var exportForm = await service.GetLessonForEditAsync(lessonId, authorId);
        exportForm!.IsPdfExportEnabled = true;
        await service.SaveLessonAsync(exportForm, authorId);

        var lessons = await service.GetLessonsForStudentAsync(studentId);
        var exportDetail = await service.GetLessonDetailForStudentPdfExportAsync(lessonId, studentId);

        Assert.True(Assert.Single(lessons).IsPdfExportEnabled);
        Assert.NotNull(exportDetail);
        Assert.Equal("Support exportable", exportDetail!.Title);
    }

    [Fact]
    public async Task SaveLessonAsync_AktualisiertUndEntferntSeiten()
    {
        using var db = TestDbContext.CreateInMemory();
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1" };
        db.Klassen.Add(level);
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var authorId = Guid.NewGuid();

        var id = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = level.Id,
            Title = "T",
            IsPublished = true,
            Pages =
            [
                new TeacherLessonPageFormModel { ContentHtml = "<p>1</p>" },
                new TeacherLessonPageFormModel { ContentHtml = "<p>2</p>" }
            ]
        }, authorId);

        var editModel = await service.GetLessonForEditAsync(id, authorId);
        Assert.NotNull(editModel);
        editModel!.Pages.RemoveAt(0);
        editModel.Pages[0].ContentHtml = "<p>2-geändert</p>";

        await service.SaveLessonAsync(editModel, authorId);

        var detail = await service.GetLessonDetailAsync(id, onlyPublished: false);
        Assert.NotNull(detail);
        Assert.Single(detail!.Pages);
        Assert.Equal(1, detail.Pages[0].PageNumber);
        Assert.Equal("<p>2-geändert</p>", detail.Pages[0].ContentHtml);
    }

    [Fact]
    public async Task CopyLessonToKlasseAsync_ErstelltNeueLektionAlsEntwurfMitSeiten()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolIdA = Guid.NewGuid();
        var schoolIdB = Guid.NewGuid();
        var authorId = Guid.NewGuid();

        var klasseA = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolIdA, Code = "CP1", Title = "CP1 A" };
        var klasseB = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolIdB, Code = "CP1", Title = "CP1 B" };
        var fachA = new Fach { Id = Guid.NewGuid(), SchoolId = schoolIdA, Name = "Math" };

        db.Schools.AddRange(
            new School { Id = schoolIdA, Name = "École A", City = "Lomé" },
            new School { Id = schoolIdB, Name = "École B", City = "Lomé" });
        db.Users.Add(new AppUser { Id = authorId, FullName = "Enseignant", UserName = "teacher" });
        db.Klassen.AddRange(klasseA, klasseB);
        db.Faecher.Add(fachA);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = authorId, SchoolId = schoolIdA, CanManageStudents = true },
            new UserSchool { AppUserId = authorId, SchoolId = schoolIdB, CanManageStudents = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var sourceId = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = klasseA.Id,
            FachId = fachA.Id,
            Title = "Support original",
            Summary = "Résumé",
            IsPublished = true,
            IsSharedWithSchool = true,
            IsPdfExportEnabled = true,
            Pages =
            [
                new TeacherLessonPageFormModel { ContentHtml = "<p>Page 1</p>" },
                new TeacherLessonPageFormModel { ContentHtml = "<p>Page 2</p>" }
            ]
        }, authorId);

        var copyId = await service.CopyLessonToKlasseAsync(sourceId, authorId, klasseB.Id);

        var copied = await service.GetLessonForEditAsync(copyId, authorId);
        Assert.NotNull(copied);
        Assert.Equal("Support original (copie)", copied!.Title);
        Assert.Equal(klasseB.Id, copied.KlasseId);
        Assert.False(copied.IsPublished);
        Assert.True(copied.IsPdfExportEnabled);
        Assert.True(copied.IsSharedWithSchool);
        Assert.Null(copied.FachId);
        Assert.Equal(2, copied.Pages.Count);
        Assert.Equal("<p>Page 1</p>", copied.Pages[0].ContentHtml);
        Assert.Equal("<p>Page 2</p>", copied.Pages[1].ContentHtml);
    }

    [Fact]
    public async Task CopyLessonToKlasseAsync_FremderAutor_WirdAbgewiesen()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var fremderId = Guid.NewGuid();
        var klasseA = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "CP1", Title = "CP1" };
        var klasseB = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "CP2", Title = "CP2" };

        db.Schools.Add(new School { Id = schoolId, Name = "École", City = "Lomé" });
        db.Users.AddRange(
            new AppUser { Id = authorId, FullName = "Auteur", UserName = "auteur" },
            new AppUser { Id = fremderId, FullName = "Autre", UserName = "autre" });
        db.Klassen.AddRange(klasseA, klasseB);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = authorId, SchoolId = schoolId, CanManageStudents = true },
            new UserSchool { AppUserId = fremderId, SchoolId = schoolId, CanManageStudents = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var sourceId = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = klasseA.Id,
            Title = "Support",
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, authorId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CopyLessonToKlasseAsync(sourceId, fremderId, klasseB.Id));
    }

    [Fact]
    public async Task CopyLessonToKlasseAsync_PublishDirectly_SetztVeroeffentlicht()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var klasseA = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "CP1", Title = "CP1" };
        var klasseB = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "CP2", Title = "CP2" };

        db.Schools.Add(new School { Id = schoolId, Name = "École", City = "Lomé" });
        db.Users.Add(new AppUser { Id = authorId, FullName = "Auteur", UserName = "auteur" });
        db.Klassen.AddRange(klasseA, klasseB);
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);
        var sourceId = await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = klasseA.Id,
            Title = "Support",
            IsPublished = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>x</p>" }]
        }, authorId);

        var copyId = await service.CopyLessonToKlasseAsync(sourceId, authorId, klasseB.Id, publishDirectly: true);

        var copied = await service.GetLessonDetailAsync(copyId, onlyPublished: false);
        Assert.NotNull(copied);
        Assert.True(copied!.IsPublished);
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_MarkiertAktuelleKlasseUndHistorie()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var currentKlasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "CP1", Title = "CP1 A" };
        var oldKlasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolId, Code = "CP0", Title = "CP0 A" };
        var studentId = Guid.NewGuid();

        db.Schools.Add(new School { Id = schoolId, Name = "École", City = "Lomé" });
        db.Users.Add(new AppUser { Id = authorId, FullName = "Professeur A", UserName = "prof-a" });
        db.Klassen.AddRange(currentKlasse, oldKlasse);
        db.UserSchools.Add(new UserSchool { AppUserId = authorId, SchoolId = schoolId, CanManageStudents = true });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = schoolId,
            CurrentKlasseId = currentKlasse.Id,
            CurrentLevelCode = currentKlasse.Code
        });
        db.LevelEnrollments.AddRange(
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = currentKlasse.Id, IsUnlocked = true },
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = oldKlasse.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var service = new TeacherLessonService(db);

        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = currentKlasse.Id,
            Title = "Actuel",
            IsPublished = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>Actuel</p>" }]
        }, authorId);

        await service.SaveLessonAsync(new TeacherLessonFormModel
        {
            KlasseId = oldKlasse.Id,
            Title = "Historique",
            IsPublished = true,
            Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>Ancien</p>" }]
        }, authorId);

        var result = await service.GetLessonsForStudentAsync(studentId);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Title == "Actuel" && item.IsCurrentKlasse && item.KlasseTitle == "CP1 A");
        Assert.Contains(result, item => item.Title == "Historique" && !item.IsCurrentKlasse && item.KlasseTitle == "CP0 A");
    }
}
