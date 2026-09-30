using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="ExamService"/> mit In-Memory-Datenbank.
/// </summary>
public class ExamServiceTests
{
    private static async Task<(Exam exam, ExamQuestionOption richtig)> SeedExamAsync(TestDbContext db)
    {
        var school = new School { Id = Guid.NewGuid(), Name = "École", IsActive = true };
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id };
        db.Schools.Add(school);
        db.Klassen.Add(level);

        var richtig = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "Guten Tag", IsCorrect = true, Order = 1 };
        var falsch = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "Gute Nacht", IsCorrect = false, Order = 2 };

        var frage = new ExamQuestion
        {
            Id = Guid.NewGuid(),
            Prompt = "Was heißt 'Bonjour'?",
            Order = 1,
            Options = { richtig, falsch }
        };

        var exam = new Exam
        {
            Id = Guid.NewGuid(),
            KlasseId = level.Id,
            Title = "A1-Prüfung",
            Description = "Grundwortschatz",
            DurationInMinutes = 30,
            PassingScore = 50,
            Questions = { frage },
            IsPublishedToStudents = true
        };

        db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = school.Id,
            KlasseId = level.Id,
            NiveauPreis = 7500,
            StartDatum = DateTime.UtcNow.AddDays(-1),
            ErstelltVonUserID = Guid.NewGuid()
        });

        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        return (exam, richtig);
    }

    [Fact]
    public async Task StartExamAsync_ErstelltVersuchUndLiefertFragen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, _) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        // Act
        var session = await service.StartExamAsync(exam.Id, studentId);

        // Assert
        Assert.NotNull(session);
        Assert.Equal("A1-Prüfung", session!.Title);
        Assert.Single(session.Questions);
        Assert.Single(db.ExamAttempts);
    }

    [Fact]
    public async Task StartExamAsync_SetztOffenenVersuchMitAntwortUndUrspruenglicherFristFort()
    {
        using var db = TestDbContext.CreateInMemory();
        var (exam, option) = await SeedExamAsync(db);
        var studentId = Guid.NewGuid();
        var schoolId = db.Klassen.Single(k => k.Id == exam.KlasseId).SchoolId!.Value;
        db.StudentProfiles.Add(new StudentProfile { AppUserId = studentId, SchoolId = schoolId, CurrentKlasseId = exam.KlasseId });
        await db.SaveChangesAsync();
        var service = new ExamService(db);

        var first = (await service.StartExamAsync(exam.Id, studentId))!;
        Assert.True(await service.SaveDraftAsync(first.AttemptId, studentId,
            [new ExamAnswerSubmission { QuestionId = first.Questions[0].QuestionId, SelectedOptionId = option.Id }]));
        var resumed = (await service.StartExamAsync(exam.Id, studentId))!;

        Assert.True(resumed.IsResumed);
        Assert.Equal(first.AttemptId, resumed.AttemptId);
        Assert.Equal(first.DeadlineUtc, resumed.DeadlineUtc);
        Assert.Equal(option.Id, Assert.Single(resumed.SavedAnswers).SelectedOptionId);
        Assert.Single(db.ExamAttempts);
        await service.SubmitExamAsync(first.AttemptId, studentId, []);
        Assert.Single(db.ExamAnswers);
        Assert.True(db.ExamAttempts.Single().IsPassed);
    }

    [Fact]
    public async Task StartExamAsync_NachFristende_BewertetGespeichertenEntwurfUndStartetNichtNeu()
    {
        using var db = TestDbContext.CreateInMemory();
        var (exam, option) = await SeedExamAsync(db);
        var studentId = Guid.NewGuid();
        db.StudentProfiles.Add(new StudentProfile { AppUserId = studentId, SchoolId = db.Klassen.Single(k => k.Id == exam.KlasseId).SchoolId!.Value, CurrentKlasseId = exam.KlasseId });
        await db.SaveChangesAsync();
        var service = new ExamService(db);
        var first = (await service.StartExamAsync(exam.Id, studentId))!;
        await service.SaveDraftAsync(first.AttemptId, studentId,
            [new ExamAnswerSubmission { QuestionId = first.Questions[0].QuestionId, SelectedOptionId = option.Id }]);
        db.ExamAttempts.Single().DeadlineUtc = DateTime.UtcNow.AddSeconds(-10);
        await db.SaveChangesAsync();

        Assert.Null(await service.StartExamAsync(exam.Id, studentId));
        Assert.Single(db.ExamAttempts);
        Assert.True(db.ExamAttempts.Single().IsCompleted);
        Assert.Single(db.ExamAnswers);
    }

    [Fact]
    public async Task GetExamForStudentPdfAsync_ErstNachAbschlussUndFreigabe()
    {
        using var db = TestDbContext.CreateInMemory();
        var (exam, _) = await SeedExamAsync(db);
        exam.IsPdfExportEnabled = true;
        var studentId = Guid.NewGuid();
        db.StudentProfiles.Add(new StudentProfile { AppUserId = studentId, SchoolId = db.Klassen.Single(k => k.Id == exam.KlasseId).SchoolId!.Value, CurrentKlasseId = exam.KlasseId });
        await db.SaveChangesAsync();
        var service = new ExamService(db);
        Assert.Null(await service.GetExamForStudentPdfAsync(studentId, exam.Id));
        var session = (await service.StartExamAsync(exam.Id, studentId))!;
        Assert.Null(await service.GetExamForStudentPdfAsync(studentId, exam.Id));
        await service.SubmitExamAsync(session.AttemptId, studentId, []);
        Assert.NotNull(await service.GetExamForStudentPdfAsync(studentId, exam.Id));
        exam.IsPdfExportEnabled = false;
        await db.SaveChangesAsync();
        Assert.Null(await service.GetExamForStudentPdfAsync(studentId, exam.Id));
        Assert.Null(await service.GetExamForStudentPdfAsync(Guid.NewGuid(), exam.Id));
    }

    [Fact]
    public async Task StartExamAsync_UnbekanntePruefung_LiefertNull()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var service = new ExamService(db);

        // Act
        var session = await service.StartExamAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public async Task StartExamAsync_AktiverCycleOhneFreischaltung_LiefertSession()
    {
        using var db = TestDbContext.CreateInMemory();
        var (exam, _) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = schoolId,
            CurrentLevelCode = "A1.1",
            CurrentKlasseId = exam.KlasseId
        });
        await db.SaveChangesAsync();

        var session = await service.StartExamAsync(exam.Id, studentId);

        Assert.NotNull(session);
    }

    [Fact]
    public async Task StartExamAsync_OhneAktivenCycle_LiefertNull()
    {
        using var db = TestDbContext.CreateInMemory();
        var (exam, _) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = schoolId,
            CurrentLevelCode = "A1.1",
            CurrentKlasseId = exam.KlasseId
        });

        var durchlauf = db.KursNiveauDurchlaeufe.Single(x => x.KlasseId == exam.KlasseId);
        durchlauf.EndDatum = DateTime.UtcNow.AddHours(-1);
        await db.SaveChangesAsync();

        var session = await service.StartExamAsync(exam.Id, studentId);

        Assert.Null(session);
    }

    [Fact]
    public async Task GetExamsForStudentAsync_AktiverCycleOhneFreischaltung_LiefertExamen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (exam, _) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentId,
            SchoolId = schoolId,
            CurrentLevelCode = "A1.1",
            CurrentKlasseId = exam.KlasseId
        });
        await db.SaveChangesAsync();

        var exams = await service.GetExamsForStudentAsync(studentId, "A1.1");

        var examItem = Assert.Single(exams);
        Assert.Equal(exam.Id, examItem.ExamId);
    }

    [Fact]
    public async Task GetTeacherExamsAsync_SchliesstStandardExamenAus()
    {
        using var db = TestDbContext.CreateInMemory();
        var authorId = Guid.NewGuid();
        var standardKlasse = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Standard", IstSystemNiveau = true };
        var eigeneKlasse = new Klasse { Id = Guid.NewGuid(), Code = "EXAM", Title = "Préparation", SchoolId = Guid.NewGuid() };
        db.Klassen.AddRange(standardKlasse, eigeneKlasse);
        db.Exams.AddRange(
            new Exam { Id = Guid.NewGuid(), KlasseId = standardKlasse.Id, Title = "Test final A1.1", DurationInMinutes = 20, PassingScore = 60 },
            new Exam { Id = Guid.NewGuid(), KlasseId = eigeneKlasse.Id, AuthorId = authorId, Title = "Examen interne", DurationInMinutes = 30, PassingScore = 60 });
        await db.SaveChangesAsync();
        var service = new ExamService(db);

        var exams = await service.GetTeacherExamsAsync(authorId);

        var exam = Assert.Single(exams);
        Assert.Equal("Examen interne", exam.Title);
    }

    [Fact]
    public async Task SaveExamAsync_LehntFremdeKlasseTrotzGleichemCodeAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var authorId = Guid.NewGuid();
        var school = new School { Id = Guid.NewGuid(), Name = "École", IsActive = true };
        var otherSchool = new School { Id = Guid.NewGuid(), Name = "Autre école", IsActive = true };
        var allowed = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "A1", Title = "A1" };
        var foreign = new Klasse { Id = Guid.NewGuid(), SchoolId = otherSchool.Id, Code = "A1", Title = "A1" };
        db.Schools.AddRange(school, otherSchool);
        db.Klassen.AddRange(allowed, foreign);
        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { LehrerID = authorId, SchulID = school.Id, KlasseId = allowed.Id });
        await db.SaveChangesAsync();

        var model = new ExamFormModel
        {
            Title = "Test", NiveauCode = "A1", KlasseId = foreign.Id,
            Exercises = [new ExamExerciseFormModel { Questions = [new ExamQuestionFormModel { Prompt = "Frage", Options = [new() { Text = "Ja", IsCorrect = true }, new() { Text = "Nein" }] }] }]
        };
        var service = new ExamService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveExamAsync(authorId, model));
        Assert.Empty(db.Exams);
    }

    [Fact]
    public async Task DeleteExamAsync_LehntStandardExamenAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var authorId = Guid.NewGuid();
        var standardKlasse = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Standard", IstSystemNiveau = true };
        var exam = new Exam { Id = Guid.NewGuid(), KlasseId = standardKlasse.Id, AuthorId = authorId, Title = "Test final A1.1", DurationInMinutes = 20, PassingScore = 60 };
        db.Klassen.Add(standardKlasse);
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        var service = new ExamService(db);

        var (succeeded, _) = await service.DeleteExamAsync(authorId, exam.Id);

        Assert.False(succeeded);
        Assert.Single(db.Exams);
    }

    [Fact]
    public async Task SubmitExamAsync_KorrekteAntwort_WirdAlsBestandenGewertet()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, richtig) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        var session = await service.StartExamAsync(exam.Id, studentId);
        var frageId = session!.Questions[0].QuestionId;

        var antworten = new Dictionary<Guid, Guid> { [frageId] = richtig.Id };

        // Act
        var result = await service.SubmitExamAsync(session.AttemptId, studentId, antworten);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.IsPassed);
        Assert.Equal(1, result.CorrectAnswers);
        Assert.Equal(100, result.ScorePercent);
    }

    [Fact]
    public async Task SubmitExamAsync_UnbekannterVersuch_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var service = new ExamService(db);

        // Act
        var result = await service.SubmitExamAsync(Guid.NewGuid(), Guid.NewGuid(), new Dictionary<Guid, Guid>());

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SubmitExamAsync_MultipleChoice_GenauKorrekteOptionen_IstRichtig()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, richtig1, richtig2, _) = await SeedMultipleChoiceExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        var session = await service.StartExamAsync(exam.Id, studentId);
        var frageId = session!.Questions[0].QuestionId;

        var submissions = new List<ExamAnswerSubmission>
        {
            new() { QuestionId = frageId, SelectedOptionIds = [richtig1.Id, richtig2.Id] }
        };

        // Act
        var result = await service.SubmitExamAsync(session.AttemptId, studentId, submissions);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.CorrectAnswers);
        Assert.Equal(100, result.ScorePercent);
    }

    [Fact]
    public async Task SubmitExamAsync_MultipleChoice_ZusaetzlicheFalscheOption_IstFalsch()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, richtig1, richtig2, falsch) = await SeedMultipleChoiceExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        var session = await service.StartExamAsync(exam.Id, studentId);
        var frageId = session!.Questions[0].QuestionId;

        var submissions = new List<ExamAnswerSubmission>
        {
            new() { QuestionId = frageId, SelectedOptionIds = [richtig1.Id, richtig2.Id, falsch.Id] }
        };

        // Act
        var result = await service.SubmitExamAsync(session.AttemptId, studentId, submissions);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.CorrectAnswers);
    }

    [Fact]
    public async Task SubmitExamAsync_MultipleChoice_UnvollstaendigeAuswahl_IstFalsch()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, richtig1, _, _) = await SeedMultipleChoiceExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();

        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        var session = await service.StartExamAsync(exam.Id, studentId);
        var frageId = session!.Questions[0].QuestionId;

        var submissions = new List<ExamAnswerSubmission>
        {
            new() { QuestionId = frageId, SelectedOptionIds = [richtig1.Id] }
        };

        // Act
        var result = await service.SubmitExamAsync(session.AttemptId, studentId, submissions);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.CorrectAnswers);
    }

    [Fact]
    public async Task GetKlassenAbschlussUebersichtAsync_AktiverFolgezyklusIgnoriertHistorischeSchuelerUndAlteExams()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "A1", Title = "A1", SchoolId = school.Id, IstSystemNiveau = false };
        var oldStudent = new AppUser { Id = Guid.NewGuid(), UserName = "old", FullName = "Ancien élève" };
        var currentStudent = new AppUser { Id = Guid.NewGuid(), UserName = "current", FullName = "Élève actuel" };
        var closedDurchlauf = new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = school.Id,
            KlasseId = klasse.Id,
            StartDatum = DateTime.UtcNow.AddYears(-1),
            EndDatum = DateTime.UtcNow.AddMonths(-10),
            NiveauPreis = 1000m
        };
        var activeDurchlauf = new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = school.Id,
            KlasseId = klasse.Id,
            StartDatum = DateTime.UtcNow.AddMonths(-1),
            NiveauPreis = 1000m
        };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Users.AddRange(oldStudent, currentStudent);
        db.StudentProfiles.AddRange(
            new StudentProfile { Id = Guid.NewGuid(), AppUserId = oldStudent.Id, SchoolId = school.Id, CurrentKlasseId = Guid.NewGuid(), CurrentLevelCode = "A2" },
            new StudentProfile { Id = Guid.NewGuid(), AppUserId = currentStudent.Id, SchoolId = school.Id, CurrentKlasseId = klasse.Id, CurrentLevelCode = klasse.Code });
        db.LevelEnrollments.AddRange(
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = oldStudent.Id, KlasseId = klasse.Id },
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = currentStudent.Id, KlasseId = klasse.Id });
        db.KursNiveauDurchlaeufe.AddRange(closedDurchlauf, activeDurchlauf);
        db.KlassenAbschlussEntscheidungen.Add(new KlassenAbschlussEntscheidung { DurchlaufId = closedDurchlauf.Id, StudentId = oldStudent.Id, IsBestanden = true });
        await db.SaveChangesAsync();
        var service = new ExamService(db);

        var (saved, message) = await service.SetManuelleAbschlussNoteAsync(activeDurchlauf.Id, currentStudent.Id, "16 de Moyenne", Guid.NewGuid());
        Assert.True(saved, message);

        var uebersicht = await service.GetKlassenAbschlussUebersichtAsync(activeDurchlauf.Id);

        Assert.NotNull(uebersicht);
        var student = Assert.Single(uebersicht!.Schueler);
        Assert.Equal(currentStudent.Id, student.StudentId);
        Assert.Equal("16 de Moyenne", student.AbschlussNoteText);
    }

    [Fact]
    public async Task GetKlassenAbschlussUebersichtAsync_GeschlossenerDurchlaufNutztHistorischeEntscheidungskohorte()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "A1", Title = "A1", SchoolId = school.Id, IstSystemNiveau = false };
        var student = new AppUser { Id = Guid.NewGuid(), UserName = "old", FullName = "Historischer Schüler" };
        var durchlauf = new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = school.Id,
            KlasseId = klasse.Id,
            StartDatum = DateTime.UtcNow.AddYears(-1),
            EndDatum = DateTime.UtcNow.AddMonths(-10),
            NiveauPreis = 1000m
        };
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.Users.Add(student);
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = student.Id, SchoolId = school.Id, CurrentKlasseId = Guid.NewGuid(), CurrentLevelCode = "A2" });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = student.Id, KlasseId = klasse.Id });
        db.KursNiveauDurchlaeufe.Add(durchlauf);
        db.KlassenAbschlussEntscheidungen.Add(new KlassenAbschlussEntscheidung { DurchlaufId = durchlauf.Id, StudentId = student.Id, IsBestanden = true, AbschlussNoteText = "10/20" });
        await db.SaveChangesAsync();
        var service = new ExamService(db);

        var uebersicht = await service.GetKlassenAbschlussUebersichtAsync(durchlauf.Id);

        Assert.NotNull(uebersicht);
        var result = Assert.Single(uebersicht!.Schueler);
        Assert.Equal(student.Id, result.StudentId);
        Assert.Equal("10/20", result.AbschlussNoteText);
        Assert.True(result.IsBestanden);
    }

    private static async Task<(Exam exam, ExamQuestionOption richtig1, ExamQuestionOption richtig2, ExamQuestionOption falsch)> SeedMultipleChoiceExamAsync(TestDbContext db)
    {
        var school = new School { Id = Guid.NewGuid(), Name = "École", IsActive = true };
        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1", SchoolId = school.Id };
        db.Schools.Add(school);
        db.Klassen.Add(level);

        var richtig1 = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "der", IsCorrect = true, Order = 1 };
        var richtig2 = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "die", IsCorrect = true, Order = 2 };
        var falsch = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "das", IsCorrect = false, Order = 3 };

        var frage = new ExamQuestion
        {
            Id = Guid.NewGuid(),
            Prompt = "Welche Artikel sind richtig?",
            QuestionType = ExamQuestionType.MultipleChoice,
            Order = 1,
            Options = { richtig1, richtig2, falsch }
        };

        var exam = new Exam
        {
            Id = Guid.NewGuid(),
            KlasseId = level.Id,
            Title = "A1-Prüfung",
            Description = "Artikel",
            DurationInMinutes = 30,
            PassingScore = 50,
            Questions = { frage },
            IsPublishedToStudents = true
        };

        db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = school.Id,
            KlasseId = level.Id,
            NiveauPreis = 7500,
            StartDatum = DateTime.UtcNow.AddDays(-1),
            ErstelltVonUserID = Guid.NewGuid()
        });

        db.Exams.Add(exam);
        await db.SaveChangesAsync();

        return (exam, richtig1, richtig2, falsch);
    }

    [Fact]
    public async Task SubmitExamAsync_NachAblaufDerFrist_WirdNichtBestanden()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, richtig) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();
        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        var session = await service.StartExamAsync(exam.Id, studentId);

        // Deadline in die Vergangenheit setzen, um eine Zeitüberschreitung zu erzwingen.
        var attempt = db.ExamAttempts.Single(a => a.Id == session!.AttemptId);
        attempt.DeadlineUtc = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var antworten = new Dictionary<Guid, Guid> { [session!.Questions[0].QuestionId] = richtig.Id };

        // Act
        var result = await service.SubmitExamAsync(session.AttemptId, studentId, antworten);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.False(result.IsPassed);
    }

    [Fact]
    public async Task SubmitExamAsync_BereitsAbgeschlossen_LiefertVorhandenesErgebnis()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (exam, richtig) = await SeedExamAsync(db);
        var service = new ExamService(db);
        var studentId = Guid.NewGuid();
        var schoolId = db.Klassen.Where(x => x.Id == exam.KlasseId).Select(x => x.SchoolId).Single()!.Value;
        db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = studentId, SchoolId = schoolId, CurrentLevelCode = "A1.1", CurrentKlasseId = exam.KlasseId });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = studentId, KlasseId = exam.KlasseId, IsUnlocked = true });
        db.SaveChanges();

        var session = await service.StartExamAsync(exam.Id, studentId);
        var antworten = new Dictionary<Guid, Guid> { [session!.Questions[0].QuestionId] = richtig.Id };
        await service.SubmitExamAsync(session.AttemptId, studentId, antworten);

        // Act
        var result = await service.SubmitExamAsync(session.AttemptId, studentId, antworten);

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.IsPassed);
    }
}
