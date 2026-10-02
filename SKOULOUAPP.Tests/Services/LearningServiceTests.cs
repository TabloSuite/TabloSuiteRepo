using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für <see cref="LearningService"/>, insbesondere die Niveau-Freischaltung.
/// </summary>
public class LearningServiceTests
{
    [Fact]
    public async Task GetLessonsForStudentAsync_AutorloseLektionenFilternNachKlasseUndFach()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CP1A", Title = "CP1A" };
        var fach = new Fach { Id = Guid.NewGuid(), SchoolId = school.Id, Name = "Histoire" };
        var teacher = new AppUser { Id = Guid.NewGuid(), UserName = "prof-histoire", FullName = "Prof Histoire" };
        var otherTeacher = new AppUser { Id = Guid.NewGuid(), UserName = "prof-maths", FullName = "Prof Maths" };
        var studentId = Guid.NewGuid();
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Faecher.Add(fach);
        db.Users.AddRange(teacher, otherTeacher);
        db.LehrerNiveauZuordnungen.AddRange(
            new LehrerNiveauZuordnung { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = klasse.Id, LehrerID = teacher.Id },
            new LehrerNiveauZuordnung { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = klasse.Id, LehrerID = otherTeacher.Id });
        db.LehrerFachZuordnungen.Add(new LehrerFachZuordnung { Id = Guid.NewGuid(), FachId = fach.Id, LehrerId = teacher.Id });
        db.Lessons.Add(new Lesson { Id = Guid.NewGuid(), KlasseId = klasse.Id, FachId = fach.Id, Title = "Histoire", IsPublished = true });
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = school.Id, CurrentKlasseId = klasse.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = klasse.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var lesson = Assert.Single(await new LearningService(db).GetLessonsForStudentAsync(studentId));

        Assert.Null(lesson.TeacherId);
        Assert.Equal(string.Empty, lesson.TeacherName);
        Assert.Equal([teacher.Id], lesson.FilterTeacherIds);
        Assert.Equal((teacher.Id, teacher.FullName), Assert.Single(lesson.FilterTeachers));
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_ZeigtNurLektionenFreigeschalteterNiveaus()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();

        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Yaoundé" };
        var levelA1 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id, IstSystemNiveau = true };
        var levelA2 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2", SchoolId = school.Id, IstSystemNiveau = true };
        db.Schools.Add(school);
        db.Klassen.AddRange(levelA1, levelA2);

        db.Lessons.Add(new Lesson { Id = Guid.NewGuid(), KlasseId = levelA1.Id, Title = "Lektion A1", Order = 1 });
        db.Lessons.Add(new Lesson { Id = Guid.NewGuid(), KlasseId = levelA2.Id, Title = "Lektion A2", Order = 1 });

        var studentId = Guid.NewGuid();
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = school.Id,
            CurrentLevelCode = "A1.1",
            CurrentKlasseId = levelA1.Id
        });
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            KlasseId = levelA1.Id,
            IsUnlocked = true
        });

        await db.SaveChangesAsync();

        var service = new LearningService(db);

        // Act
        var lessons = await service.GetLessonsForStudentAsync(studentId);

        // Assert
        Assert.Single(lessons);
        Assert.Equal("Lektion A1", lessons[0].Title);
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_AboInCefrGruppeSchaltetAlleStandardniveausDerGruppeFrei()
    {
        using var db = TestDbContext.CreateInMemory();

        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Yaoundé" };
        var a11 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1.1", SchoolId = school.Id, CEFRGroup = "A1", IstSystemNiveau = true };
        var a12 = new Klasse { Id = Guid.NewGuid(), Code = "A1.2", Title = "A1.2", SchoolId = school.Id, CEFRGroup = "A1", IstSystemNiveau = true };
        var a21 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2.1", SchoolId = school.Id, CEFRGroup = "A2", IstSystemNiveau = true };
        var studentId = Guid.NewGuid();

        db.Schools.Add(school);
        db.Klassen.AddRange(a11, a12, a21);
        db.Lessons.AddRange(
            new Lesson { Id = Guid.NewGuid(), KlasseId = a11.Id, Title = "Leçon A1.1", Order = 1 },
            new Lesson { Id = Guid.NewGuid(), KlasseId = a12.Id, Title = "Leçon A1.2", Order = 1 },
            new Lesson { Id = Guid.NewGuid(), KlasseId = a21.Id, Title = "Leçon A2.1", Order = 1 });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = school.Id,
            CurrentLevelCode = a11.Code,
            CurrentKlasseId = a11.Id
        });
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            KlasseId = a11.Id,
            IsUnlocked = true
        });
        await db.SaveChangesAsync();

        var lessons = await new LearningService(db).GetLessonsForStudentAsync(studentId);

        Assert.Equal(["Leçon A1.1"], lessons.Select(item => item.Title));
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_BehältFrüherFreigeschalteteLektionenNachNiveauwechsel()
    {
        using var db = TestDbContext.CreateInMemory();

        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var levelA1 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id, CEFRGroup = "A1", IstSystemNiveau = true };
        var levelA2 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2", SchoolId = school.Id, CEFRGroup = "A2", IstSystemNiveau = true };
        var studentId = Guid.NewGuid();

        db.Schools.Add(school);
        db.Klassen.AddRange(levelA1, levelA2);
        db.Lessons.AddRange(
            new Lesson { Id = Guid.NewGuid(), KlasseId = levelA1.Id, Title = "Lektion A1", Order = 1 },
            new Lesson { Id = Guid.NewGuid(), KlasseId = levelA2.Id, Title = "Lektion A2", Order = 1 });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = school.Id,
            CurrentLevelCode = levelA2.Code,
            CurrentKlasseId = levelA2.Id
        });
        db.LevelEnrollments.AddRange(
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = levelA1.Id, IsUnlocked = true },
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = levelA2.Id, IsUnlocked = true });
        await db.SaveChangesAsync();

        var lessons = await new LearningService(db).GetLessonsForStudentAsync(studentId);

        Assert.Equal(["Lektion A1", "Lektion A2"], lessons.Select(x => x.Title));
    }

    [Fact]
    public async Task GetLessonsForStudentAsync_OhneFreischaltung_LiefertKeineLektionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();

        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Yaoundé" };
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id };
        db.Schools.Add(school);
        db.Klassen.Add(level);
        db.Lessons.Add(new Lesson { Id = Guid.NewGuid(), KlasseId = level.Id, Title = "Lektion", Order = 1 });
        await db.SaveChangesAsync();

        var service = new LearningService(db);

        // Act
        var lessons = await service.GetLessonsForStudentAsync(Guid.NewGuid());

        // Assert
        Assert.Empty(lessons);
    }

    [Fact]
    public async Task SubmitAnswerAsync_KorrekteAntwortInFreigeschaltetemNiveau_WirdAlsRichtigGewertet()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, richtig, _) = await SeedUebungAsync(db, studentId, freigeschaltet: true);
        var service = new LearningService(db);

        // Act
        var result = await service.SubmitAnswerAsync(exercise.Id, richtig.Id, studentId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.IsCorrect);
        Assert.True(result.LessonCompleted);
        Assert.Equal(1, result.CorrectExerciseCount);
        Assert.Equal(1, result.TotalExerciseCount);
    }

    [Fact]
    public async Task SubmitAnswerAsync_FalscheAntwort_WirdAlsFalschGewertet()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, _, falsch) = await SeedUebungAsync(db, studentId, freigeschaltet: true);
        var service = new LearningService(db);

        // Act
        var result = await service.SubmitAnswerAsync(exercise.Id, falsch.Id, studentId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.IsCorrect);
        Assert.False(result.LessonCompleted);
    }

    [Fact]
    public async Task SubmitAnswerAsync_NichtFreigeschaltetesNiveau_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, richtig, _) = await SeedUebungAsync(db, studentId, freigeschaltet: false);
        var service = new LearningService(db);

        // Act
        var result = await service.SubmitAnswerAsync(exercise.Id, richtig.Id, studentId);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmitAnswerAsync_UnbekannteUebung_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var service = new LearningService(db);

        // Act
        var result = await service.SubmitAnswerAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmitExerciseAsync_MultipleChoice_GenauKorrekteOptionen_IstRichtig()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, richtig1, richtig2, _) = await SeedMultipleChoiceUebungAsync(db, studentId);
        var service = new LearningService(db);

        var submission = new ExerciseSubmission
        {
            SelectedOptionIds = [richtig1.Id, richtig2.Id]
        };

        // Act
        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, submission);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task SubmitExerciseAsync_MultipleChoice_UnvollstaendigeAuswahl_IstFalsch()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, richtig1, _, _) = await SeedMultipleChoiceUebungAsync(db, studentId);
        var service = new LearningService(db);

        var submission = new ExerciseSubmission
        {
            SelectedOptionIds = [richtig1.Id]
        };

        // Act
        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, submission);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.IsCorrect);
    }

    [Fact]
    public async Task SubmitExerciseAsync_SpeichertAssignmentVersionUndSnapshot()
    {
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, richtig1, richtig2, _) = await SeedMultipleChoiceUebungAsync(db, studentId);
        exercise.Lesson.AssignmentVersion = 3;
        await db.SaveChangesAsync();

        var service = new LearningService(db);
        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, new ExerciseSubmission
        {
            SelectedOptionIds = [richtig1.Id, richtig2.Id]
        });

        Assert.True(result.Succeeded);

        var attempt = await db.ExerciseAttempts
            .OrderByDescending(a => a.AnsweredAtUtc)
            .FirstAsync();
        Assert.Equal(3, attempt.AssignmentVersion);
        Assert.False(string.IsNullOrWhiteSpace(attempt.ExerciseSnapshotJson));
        Assert.Contains("Artikel", attempt.ExerciseSnapshotJson);
    }

    [Fact]
    public async Task GetLessonDetailAsync_BeruecksichtigtNurAbgabenDerAktuellenVersion()
    {
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, _, _, _) = await SeedMultipleChoiceUebungAsync(db, studentId);

        db.ExerciseAttempts.AddRange(
            new ExerciseAttempt
            {
                Id = Guid.NewGuid(),
                ExerciseId = exercise.Id,
                StudentId = studentId,
                AssignmentVersion = 1,
                IsCorrect = true,
                AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-10)
            },
            new ExerciseAttempt
            {
                Id = Guid.NewGuid(),
                ExerciseId = exercise.Id,
                StudentId = studentId,
                AssignmentVersion = 2,
                IsCorrect = false,
                AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-5)
            });

        exercise.Lesson.AssignmentVersion = 2;
        await db.SaveChangesAsync();

        var service = new LearningService(db);
        var detail = await service.GetLessonDetailAsync(exercise.LessonId, studentId);

        Assert.NotNull(detail);
        var vm = Assert.Single(detail!.Exercises);
        Assert.False(vm.IsAnsweredCorrectly);
        Assert.False(detail.IsCompleted);
    }

    [Fact]
    public async Task GetLessonDetailAsync_NachFristverlaengerung_ErlaubtAllenSchuelernErneuteAbgabe()
    {
        using var db = TestDbContext.CreateInMemory();
        var submittedStudentId = Guid.NewGuid();
        var waitingStudentId = Guid.NewGuid();
        var (exercise, correct1, correct2, _) = await SeedMultipleChoiceUebungAsync(db, submittedStudentId);
        exercise.Lesson.CreatedByTeacherId = Guid.NewGuid();
        exercise.Lesson.ExerciseSubmissionDueAtUtc = DateTime.UtcNow.AddMinutes(5);
        var schoolId = exercise.Lesson.Klasse.SchoolId!.Value;
        db.StudentProfiles.Add(new StudentProfile
        {
            AppUserId = waitingStudentId, SchoolId = schoolId, CurrentKlasseId = exercise.Lesson.KlasseId
        });
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = waitingStudentId, KlasseId = exercise.Lesson.KlasseId, IsUnlocked = true
        });
        await db.SaveChangesAsync();
        var learning = new LearningService(db);
        Assert.True((await learning.SubmitExerciseAsync(exercise.Id, submittedStudentId, new ExerciseSubmission
        {
            SelectedOptionIds = [correct1.Id, correct2.Id]
        })).Succeeded);
        Assert.True((await learning.GetLessonDetailAsync(exercise.LessonId, submittedStudentId))!.IsCompleted);

        exercise.Lesson.ExerciseSubmissionDueAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        Assert.False((await learning.SubmitExerciseAsync(exercise.Id, waitingStudentId, new ExerciseSubmission
        {
            SelectedOptionIds = [correct1.Id, correct2.Id]
        })).Succeeded);

        var dashboard = new DashboardService(db, TestUserManager.Create([]), new FakeMediaStorageService());
        var extended = await dashboard.ChangeLessonSubmissionDueAsync(exercise.LessonId, DateTime.UtcNow.AddDays(1), exercise.Lesson.CreatedByTeacherId!.Value);

        Assert.True(extended.Succeeded, extended.Message);
        Assert.Equal(2, exercise.Lesson.AssignmentVersion);
        var openLessons = await learning.GetLessonsForStudentAsync(submittedStudentId);
        Assert.Equal(1, openLessons.Count(x => x.ExerciseCount > 0 && !x.IsCompleted));
        Assert.Equal(exercise.Lesson.ExerciseSubmissionDueAtUtc, Assert.Single(openLessons).ExerciseSubmissionDueAtUtc);
        var previousDetail = (await learning.GetLessonDetailAsync(exercise.LessonId, submittedStudentId))!;
        Assert.False(previousDetail.IsCompleted);
        Assert.False(Assert.Single(previousDetail.Exercises).HasSubmittedAttempt);
        Assert.False((await learning.GetLessonDetailAsync(exercise.LessonId, waitingStudentId))!.IsCompleted);
        Assert.True((await learning.SubmitExerciseAsync(exercise.Id, submittedStudentId, new ExerciseSubmission
        {
            SelectedOptionIds = [correct1.Id, correct2.Id]
        })).Succeeded);
        Assert.True((await learning.SubmitExerciseAsync(exercise.Id, waitingStudentId, new ExerciseSubmission
        {
            SelectedOptionIds = [correct1.Id, correct2.Id]
        })).Succeeded);
        Assert.Equal(1, db.ExerciseAttempts.Count(a => a.AssignmentVersion == 1));
        Assert.Equal(2, db.ExerciseAttempts.Count(a => a.AssignmentVersion == 2));
    }

    [Fact]
    public async Task SubmitExerciseAsync_MultipleChoice_ZusaetzlicheFalscheOption_IstFalsch()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, richtig1, richtig2, falsch) = await SeedMultipleChoiceUebungAsync(db, studentId);
        var service = new LearningService(db);

        var submission = new ExerciseSubmission
        {
            SelectedOptionIds = [richtig1.Id, richtig2.Id, falsch.Id]
        };

        // Act
        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, submission);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(result.IsCorrect);
    }

    [Fact]
    public async Task SubmitExerciseAsync_MultipleChoice_OhneAuswahl_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var (exercise, _, _, _) = await SeedMultipleChoiceUebungAsync(db, studentId);
        var service = new LearningService(db);

        var submission = new ExerciseSubmission { SelectedOptionIds = [] };

        // Act
        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, submission);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("<p>Ich heiße Anna</p>", "Ich heiße Anna")]
    [InlineData("<p>ich bin Paul</p>", "ich bin Paul")]
    public async Task SubmitExerciseAsync_SentenceBuilding_AkzeptiertRichTextHtmlAntwort(string studentAnswer, string acceptedAnswer)
    {
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var exercise = await SeedSentenceBuildingExerciseAsync(db, studentId, acceptedAnswer);
        var service = new LearningService(db);

        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, new ExerciseSubmission
        {
            TextAnswer = studentAnswer
        });

        Assert.True(result.Succeeded);
        Assert.True(result.IsCorrect);
    }

    [Fact]
    public async Task SubmitExerciseAsync_SentenceBuilding_BewertetGrossUndKleinschreibung()
    {
        using var db = TestDbContext.CreateInMemory();
        var studentId = Guid.NewGuid();
        var exercise = await SeedSentenceBuildingExerciseAsync(db, studentId, "Ich bin Paul");
        var service = new LearningService(db);

        var result = await service.SubmitExerciseAsync(exercise.Id, studentId, new ExerciseSubmission
        {
            TextAnswer = "<p>ich bin Paul</p>"
        });

        Assert.True(result.Succeeded);
        Assert.False(result.IsCorrect);
    }

    private static async Task<(Exercise exercise, ExerciseOption richtig1, ExerciseOption richtig2, ExerciseOption falsch)> SeedMultipleChoiceUebungAsync(
        TestDbContext db,
        Guid studentId)
    {
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Yaoundé" };
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id };
        db.Schools.Add(school);
        db.Klassen.Add(level);

        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = level.Id, Title = "Lektion", Order = 1 };
        db.Lessons.Add(lesson);

        var richtig1 = new ExerciseOption { Id = Guid.NewGuid(), Text = "der", IsCorrect = true, Order = 1 };
        var richtig2 = new ExerciseOption { Id = Guid.NewGuid(), Text = "die", IsCorrect = true, Order = 2 };
        var falsch = new ExerciseOption { Id = Guid.NewGuid(), Text = "das", IsCorrect = false, Order = 3 };

        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            Title = "Artikel",
            Question = "Welche Artikel sind richtig?",
            ExerciseType = "MultipleChoice",
            Order = 1,
            Options = { richtig1, richtig2, falsch }
        };
        db.Exercises.Add(exercise);

        db.LevelEnrollments.Add(new LevelEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            KlasseId = level.Id,
            IsUnlocked = true
        });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = school.Id,
            CurrentLevelCode = level.Code,
            CurrentKlasseId = level.Id
        });

        await db.SaveChangesAsync();

        return (exercise, richtig1, richtig2, falsch);
    }

    private static async Task<Exercise> SeedSentenceBuildingExerciseAsync(
        TestDbContext db,
        Guid studentId,
        string acceptedAnswer)
    {
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Yaoundé" };
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id };
        db.Schools.Add(school);
        db.Klassen.Add(level);

        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = level.Id, Title = "Lektion", Order = 1 };
        db.Lessons.Add(lesson);

        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            Title = "Satz bauen",
            Question = "Ordne die Wörter.",
            ExerciseType = "SentenceBuilding",
            AcceptedAnswersJson = System.Text.Json.JsonSerializer.Serialize(new[] { acceptedAnswer }),
            Order = 1
        };
        db.Exercises.Add(exercise);
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            KlasseId = level.Id,
            IsUnlocked = true
        });
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = school.Id,
            CurrentLevelCode = level.Code,
            CurrentKlasseId = level.Id
        });

        await db.SaveChangesAsync();

        return exercise;
    }

    private static async Task<(Exercise exercise, ExerciseOption richtig, ExerciseOption falsch)> SeedUebungAsync(
        TestDbContext db,
        Guid studentId,
        bool freigeschaltet)
    {
        var school = new School { Id = Guid.NewGuid(), Name = "École", City = "Yaoundé" };
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id };
        db.Schools.Add(school);
        db.Klassen.Add(level);

        var lesson = new Lesson { Id = Guid.NewGuid(), KlasseId = level.Id, Title = "Lektion", Order = 1 };
        db.Lessons.Add(lesson);

        var richtig = new ExerciseOption { Id = Guid.NewGuid(), Text = "Guten Tag", IsCorrect = true, Order = 1 };
        var falsch = new ExerciseOption { Id = Guid.NewGuid(), Text = "Gute Nacht", IsCorrect = false, Order = 2 };

        var exercise = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lesson.Id,
            Title = "Begrüßung",
            Question = "Was heißt 'Bonjour'?",
            Order = 1,
            Options = { richtig, falsch }
        };
        db.Exercises.Add(exercise);

        if (freigeschaltet)
        {
            db.LevelEnrollments.Add(new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                KlasseId = level.Id,
                IsUnlocked = true
            });
        }

        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = school.Id,
            CurrentLevelCode = level.Code,
            CurrentKlasseId = level.Id
        });

        await db.SaveChangesAsync();

        return (exercise, richtig, falsch);
    }
}
