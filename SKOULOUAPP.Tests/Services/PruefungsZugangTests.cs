using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class PruefungsZugangTests
{
    [Fact]
    public async Task OhneZyklusUndAboSindStartEntwurfUndAbgabeMoeglich()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, exam, option) = await SeedAsync(db);
        var service = new ExamService(db);
        exam.IsPdfExportEnabled = true;
        Assert.Empty(await db.LevelEnrollments.ToListAsync());
        Assert.Empty(await db.KursNiveauDurchlaeufe.ToListAsync());
        Assert.Equal(exam.Id, Assert.Single(await service.GetExamsForStudentAsync(student.Id)).ExamId);
        var session = await service.StartExamAsync(exam.Id, student.Id);
        Assert.NotNull(session);
        Assert.True(await service.SaveDraftAsync(session.AttemptId, student.Id,
            [new ExamAnswerSubmission { QuestionId = option.ExamQuestionId, SelectedOptionId = option.Id }]));
        Assert.True((await service.SubmitExamAsync(session.AttemptId, student.Id, new List<ExamAnswerSubmission>())).Succeeded);
        Assert.NotNull(await service.GetExamReviewForStudentAsync(exam.Id, student.Id));
    }

    [Theory]
    [InlineData("blocked")]
    [InlineData("archived")]
    [InlineData("school")]
    [InlineData("class")]
    [InlineData("unpublished")]
    [InlineData("superseded")]
    public async Task RechteverlustSperrtAuchBereitsLaufendenVersuch(string art)
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, profil, school, exam, option) = await SeedAsync(db);
        var service = new ExamService(db);
        var session = await service.StartExamAsync(exam.Id, student.Id);
        Assert.NotNull(session);
        switch (art)
        {
            case "blocked": student.IsBlocked = true; break;
            case "archived": profil.IsArchived = true; break;
            case "school": school.IsActive = false; break;
            case "class": profil.CurrentKlasseId = null; break;
            case "unpublished": exam.IsPublishedToStudents = false; break;
            case "superseded": exam.IsSuperseded = true; break;
        }
        await db.SaveChangesAsync();
        Assert.Empty(await service.GetExamsForStudentAsync(student.Id));
        Assert.Null(await service.StartExamAsync(exam.Id, student.Id));
        var antworten = new List<ExamAnswerSubmission> { new() { QuestionId = option.ExamQuestionId, SelectedOptionId = option.Id } };
        Assert.False(await service.SaveDraftAsync(session.AttemptId, student.Id, antworten));
        Assert.False((await service.SubmitExamAsync(session.AttemptId, student.Id, antworten)).Succeeded);
        Assert.Empty(await db.ExamAnswers.ToListAsync());
        Assert.False((await db.ExamAttempts.SingleAsync()).IsCompleted);
    }

    [Fact]
    public async Task AbgelaufeneAbgabefristIgnoriertNeueAntwortUndBewertetGespeichertenEntwurf()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, exam, option) = await SeedAsync(db);
        var service = new ExamService(db);
        var session = await service.StartExamAsync(exam.Id, student.Id);
        Assert.NotNull(session);
        exam.SubmissionDueAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var antworten = new List<ExamAnswerSubmission> { new() { QuestionId = option.ExamQuestionId, SelectedOptionId = option.Id } };
        Assert.False(await service.SaveDraftAsync(session.AttemptId, student.Id, antworten));
        var result = await service.SubmitExamAsync(session.AttemptId, student.Id, antworten);
        Assert.True(result.Succeeded); Assert.Equal(0, result.CorrectAnswers);
    }

    [Fact]
    public async Task EigeneAbgeschlosseneErgebnisseBleibenLesbarAberNichtBeiKontosperre()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, profil, _, exam, _) = await SeedAsync(db);
        exam.IsPdfExportEnabled = true;
        await db.SaveChangesAsync();
        var service = new ExamService(db);
        var session = await service.StartExamAsync(exam.Id, student.Id);
        Assert.NotNull(session);
        Assert.True((await service.SubmitExamAsync(session.AttemptId, student.Id, new List<ExamAnswerSubmission>())).Succeeded);
        Assert.NotNull(await service.GetExamForStudentPdfAsync(student.Id, exam.Id));
        student.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Null(await service.GetExamForStudentPdfAsync(student.Id, exam.Id));
        student.IsBlocked = false;
        profil.CurrentKlasseId = null; exam.IsPublishedToStudents = false;
        await db.SaveChangesAsync();
        Assert.Single(await service.GetExamsForStudentAsync(student.Id));
        Assert.NotNull(await service.GetExamReviewForStudentAsync(exam.Id, student.Id));
        Assert.Null(await service.GetExamReviewForStudentAsync(exam.Id, Guid.NewGuid()));
        student.IsBlocked = true; await db.SaveChangesAsync();
        Assert.Empty(await service.GetExamsForStudentAsync(student.Id));
        Assert.Null(await service.GetExamReviewForStudentAsync(exam.Id, student.Id));
        Assert.False((await service.SubmitExamAsync(session.AttemptId, student.Id, new List<ExamAnswerSubmission>())).Succeeded);
    }

    private static async Task<(AppUser Student, StudentProfile Profil, School Schule, Exam Pruefung, ExamQuestionOption Option)> SeedAsync(TestDbContext db)
    {
        var school = new School { Id = Guid.NewGuid(), Name = "École" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "6e", IstSystemNiveau = false };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève" };
        var profil = new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentKlasseId = klasse.Id };
        var question = new ExamQuestion { Id = Guid.NewGuid(), Prompt = "Question", Order = 1 };
        var option = new ExamQuestionOption { Id = Guid.NewGuid(), ExamQuestionId = question.Id, Text = "Oui", IsCorrect = true };
        question.Options.Add(option);
        var exam = new Exam { Id = Guid.NewGuid(), KlasseId = klasse.Id, Title = "Épreuve", DurationInMinutes = 30,
            PassingScore = 50, IsPublishedToStudents = true, Questions = { question } };
        db.Schools.Add(school); db.Klassen.Add(klasse); db.Users.Add(student); db.StudentProfiles.Add(profil); db.Exams.Add(exam);
        await db.SaveChangesAsync();
        return (student, profil, school, exam, option);
    }
}
