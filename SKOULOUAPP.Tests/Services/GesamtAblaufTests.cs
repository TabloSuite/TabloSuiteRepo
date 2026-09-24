using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.Student;
using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using HalloApp.WebApp.Models.Auth;
using HalloApp.WebApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Durchgehender End-to-End-Test, der den kompletten Lebenszyklus der App
/// in einem einzigen Ablauf abbildet:
/// Schule + Schulleiter -> Lehrer-Zuordnung -> Schüler-Selbstregistrierung (blockiert)
/// -> Freischaltung -> Abo-Zuweisung + Bezahlung -> Abrechnung/Periodenabschluss
/// -> Schüler-Dashboard. Entspricht der manuellen Testliste in
/// docs/Testliste-Registrierung-bis-Schueler.md.
/// </summary>
public class GesamtAblaufTests
{
    [Fact]
    public async Task Schulstartfluss_RegistrierungFreigabeKlassenLehrerSchuelerAufgabenUndScores_FunktioniertAlsEinAblauf()
    {
        var databaseName = Guid.NewGuid().ToString();
        var factory = new TestApplicationDbContextFactory(databaseName);
        await using var db = factory.CreateDbContext();

        var admin = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = "admin@hallo.tg",
            Email = "admin@hallo.tg",
            FullName = "Admin Central"
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var emailService = new FakeEmailService();
        var userManager = TestUserManager.Create([admin]);
        userManager.SeedRole(admin, Rollen.SuperAdmin);
        userManager.OnUserCreated = user =>
        {
            if (db.Users.Local.All(x => x.Id != user.Id) && !db.Users.Any(x => x.Id == user.Id))
            {
                db.Users.Add(user);
                db.SaveChanges();
            }
        };
        userManager.OnUserUpdated = user =>
        {
            if (db.Users.Local.All(x => x.Id != user.Id) && !db.Users.Any(x => x.Id == user.Id))
            {
                db.Users.Add(user);
            }

            db.SaveChanges();
        };
        userManager.OnUserDeleted = user =>
        {
            var existing = db.Users.Local.FirstOrDefault(x => x.Id == user.Id) ?? db.Users.FirstOrDefault(x => x.Id == user.Id);
            if (existing is not null)
            {
                db.Users.Remove(existing);
                db.SaveChanges();
            }
        };

        var authService = new AuthService(
            db,
            factory,
            userManager,
            new TestSignInManager(userManager),
            emailService,
            Options.Create(new HalloApp.Core.Options.PostmarkOptions { Enabled = false }),
            new ConfigurationBuilder().Build(),
            NullLogger<AuthService>.Instance);

        var registerResult = await authService.RegisterSchulLeiterAsync(new RegisterInputModel
        {
            Vorname = "Afi",
            Nachname = "Mensah",
            Email = "directrice@test.tg",
            TelefonNummer = "+22890000001",
            SchulName = "École Lumière",
            SchulStadt = "Lomé",
            SchulLand = "Togo",
            Password = "Start123!",
            ConfirmPassword = "Start123!",
            AgbAccepted = true
        });

        Assert.True(registerResult.Succeeded, registerResult.ErrorMessage);

        var registeredDirector = await userManager.FindByEmailAsync("directrice@test.tg");
        Assert.NotNull(registeredDirector);

        var director = await db.Users.SingleAsync(x => x.Email == "directrice@test.tg", CancellationToken.None);
        var school = await db.Schools.SingleAsync(x => x.Name == "École Lumière", CancellationToken.None);
        Assert.True(director.IsBlocked);
        Assert.True(director.IsSelfRegistered);
        Assert.Contains(Rollen.SchulLeiter, await userManager.GetRolesAsync(director));
        Assert.Contains(Rollen.Lehrer, await userManager.GetRolesAsync(director));
        Assert.True(await db.UserSchools.AnyAsync(x => x.AppUserId == director.Id && x.SchoolId == school.Id && x.CanManageStudents, CancellationToken.None));
        Assert.Single(emailService.Gesendet);

        director.IsBlocked = false;
        director.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var directorLogin = await authService.PasswordSignInAsync("directrice@test.tg", "Start123!", rememberMe: false);
        Assert.True(directorLogin.Succeeded, directorLogin.ErrorMessage);
        Assert.NotNull(registeredDirector!.ActiveSessionId);

        var dashboard = new DashboardService(db, userManager, new FakeMediaStorageService());
        var learning = new LearningService(db);

        var klasseCp1 = await dashboard.SaveSchoolKlasseAsync(school.Id, new CourseFormModel
        {
            Title = "CP1A",
            CEFRGroup = "Classe",
            Description = "Classe d'initiation",
            NeueKlassenStufe = "CP1",
            RecommendedLessonCount = 6,
            Price = 12500m
        });

        var klasseCp2 = await dashboard.SaveSchoolKlasseAsync(school.Id, new CourseFormModel
        {
            Title = "CP2A",
            CEFRGroup = "Classe",
            Description = "Classe de consolidation",
            NeueKlassenStufe = "CP2",
            RecommendedLessonCount = 8,
            Price = 13000m
        });

        Assert.True(klasseCp1.Succeeded, klasseCp1.Message);
        Assert.True(klasseCp2.Succeeded, klasseCp2.Message);
        Assert.NotNull(klasseCp1.CourseId);
        Assert.NotNull(klasseCp2.CourseId);

        var teacherCreate = await dashboard.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Kokou",
            Nachname = "Aklassou",
            Email = "enseignant@test.tg",
            Password = "Start123!",
            PasswordConfirmation = "Start123!",
            RoleName = Rollen.Lehrer,
            SchoolIds = [school.Id],
            TeacherClassAssignments =
            [
                new TeacherClassAssignmentFormModel { SchoolId = school.Id, KlasseId = klasseCp1.CourseId!.Value, Wochenstunden = 6m },
                new TeacherClassAssignmentFormModel { SchoolId = school.Id, KlasseId = klasseCp2.CourseId!.Value, Wochenstunden = 4m }
            ]
        });

        Assert.True(teacherCreate.Succeeded, teacherCreate.Message);

        var registeredTeacher = await userManager.FindByEmailAsync("enseignant@test.tg");
        Assert.NotNull(registeredTeacher);

        var teacher = await db.Users.SingleAsync(x => x.Email == "enseignant@test.tg", CancellationToken.None);
        Assert.Contains(Rollen.Lehrer, await userManager.GetRolesAsync(teacher));
        Assert.True(await db.UserSchools.AnyAsync(x => x.AppUserId == teacher.Id && x.SchoolId == school.Id, CancellationToken.None));
        var teacherAssignments = await db.LehrerNiveauZuordnungen
            .Where(x => x.LehrerID == teacher.Id)
            .OrderBy(x => x.Wochenstunden)
            .ToListAsync();
        Assert.Equal(2, teacherAssignments.Count);

        var teacherLogin = await authService.PasswordSignInAsync("enseignant@test.tg", "Start123!", rememberMe: true);
        Assert.True(teacherLogin.Succeeded, teacherLogin.ErrorMessage);

        var studentCreateA = await dashboard.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Ama",
            Nachname = "Savi",
            Email = "ama.savi@test.tg",
            Password = "Start123!",
            PasswordConfirmation = "Start123!",
            RoleName = Rollen.Schueler,
            SchoolIds = [school.Id],
            CurrentKlasseId = klasseCp1.CourseId
        });

        var studentCreateB = await dashboard.SaveSchoolUserAsync(director.Id, new UserFormModel
        {
            Vorname = "Yao",
            Nachname = "Tete",
            Email = "yao.tete@test.tg",
            Password = "Start123!",
            PasswordConfirmation = "Start123!",
            RoleName = Rollen.Schueler,
            SchoolIds = [school.Id],
            CurrentKlasseId = klasseCp2.CourseId
        });

        Assert.True(studentCreateA.Succeeded, studentCreateA.Message);
        Assert.True(studentCreateB.Succeeded, studentCreateB.Message);

        var registeredStudentA = await userManager.FindByEmailAsync("ama.savi@test.tg");
        var registeredStudentB = await userManager.FindByEmailAsync("yao.tete@test.tg");
        Assert.NotNull(registeredStudentA);
        Assert.NotNull(registeredStudentB);

        var studentA = await db.Users.SingleAsync(x => x.Email == "ama.savi@test.tg", CancellationToken.None);
        var studentB = await db.Users.SingleAsync(x => x.Email == "yao.tete@test.tg", CancellationToken.None);
        Assert.False(studentA.IsBlocked);
        Assert.False(studentB.IsBlocked);

        var lessonA1 = new Lesson
        {
            Id = Guid.NewGuid(),
            KlasseId = klasseCp1.CourseId!.Value,
            Title = "Alphabet et salutations",
            Summary = "Premiers mots utiles",
            Content = "Bonjour, bonsoir, merci.",
            Order = 1
        };

        var lessonA2 = new Lesson
        {
            Id = Guid.NewGuid(),
            KlasseId = klasseCp2.CourseId!.Value,
            Title = "Lecture guidée",
            Summary = "Compréhension simple",
            Content = "Lis puis réponds.",
            Order = 1
        };

        var correctA1 = new ExerciseOption { Id = Guid.NewGuid(), Text = "Bonjour", IsCorrect = true, Order = 1 };
        var wrongA1 = new ExerciseOption { Id = Guid.NewGuid(), Text = "Au revoir", IsCorrect = false, Order = 2 };
        var exerciseA1 = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lessonA1.Id,
            Title = "Salutation correcte",
            Instructions = "Choisis la bonne réponse.",
            ExerciseType = "MultipleChoice",
            Question = "Que dit-on le matin ?",
            Order = 1,
            Options = { correctA1, wrongA1 }
        };

        var correctA2 = new ExerciseOption { Id = Guid.NewGuid(), Text = "Je lis.", IsCorrect = true, Order = 1 };
        var wrongA2 = new ExerciseOption { Id = Guid.NewGuid(), Text = "Je dors.", IsCorrect = false, Order = 2 };
        var exerciseA2 = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lessonA2.Id,
            Title = "Action correcte",
            Instructions = "Lis et réponds.",
            ExerciseType = "MultipleChoice",
            Question = "Que fais-tu avec un livre ?",
            Order = 1,
            Options = { correctA2, wrongA2 }
        };

        var examA1 = new Exam
        {
            Id = Guid.NewGuid(),
            KlasseId = klasseCp1.CourseId.Value,
            Title = "Évaluation CP1A",
            Description = "Contrôle de démarrage",
            DurationInMinutes = 20,
            PassingScore = 50,
            IsPublishedToStudents = true,
            Questions =
            {
                new ExamQuestion
                {
                    Id = Guid.NewGuid(),
                    Prompt = "Choisis la bonne formule.",
                    Order = 1,
                    Options =
                    {
                        new ExamQuestionOption { Id = Guid.NewGuid(), Text = "Bonjour", IsCorrect = true, Order = 1 },
                        new ExamQuestionOption { Id = Guid.NewGuid(), Text = "Bonne nuit", IsCorrect = false, Order = 2 }
                    }
                }
            }
        };

        db.Lessons.AddRange(lessonA1, lessonA2);
        db.Exercises.AddRange(exerciseA1, exerciseA2);
        db.Exams.Add(examA1);

        var enrollmentA = await db.LevelEnrollments.SingleAsync(
            enrollment => enrollment.StudentId == studentA.Id && enrollment.KlasseId == klasseCp1.CourseId.Value);
        enrollmentA.IsUnlocked = true;
        enrollmentA.IsPaid = true;
        enrollmentA.AboTyp = AboTyp.HalloBasis;
        enrollmentA.PriceAtEnrollment = 5000m;
        enrollmentA.PaidAtUtc = DateTime.UtcNow.AddDays(-21);

        var enrollmentB = await db.LevelEnrollments.SingleAsync(
            enrollment => enrollment.StudentId == studentB.Id && enrollment.KlasseId == klasseCp2.CourseId.Value);
        enrollmentB.IsUnlocked = true;
        enrollmentB.IsPaid = false;
        enrollmentB.AboTyp = AboTyp.HalloBasis;
        enrollmentB.PriceAtEnrollment = 5000m;
        db.LessonProgresses.Add(new LessonProgress
        {
            Id = Guid.NewGuid(),
            LessonId = lessonA1.Id,
            StudentId = studentA.Id,
            IsCompleted = true,
            LastActivityUtc = DateTime.UtcNow
        });
        db.ExerciseAttempts.AddRange(
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = exerciseA1.Id, StudentId = studentA.Id, IsCorrect = true, RequiresManualGrading = false, AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-20) },
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = exerciseA1.Id, StudentId = studentA.Id, IsCorrect = false, RequiresManualGrading = false, AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-10) },
            new ExerciseAttempt { Id = Guid.NewGuid(), ExerciseId = exerciseA2.Id, StudentId = studentB.Id, IsCorrect = true, RequiresManualGrading = false, AnsweredAtUtc = DateTime.UtcNow.AddMinutes(-5) });
        db.ExamAttempts.Add(new ExamAttempt
        {
            Id = Guid.NewGuid(),
            ExamId = examA1.Id,
            StudentId = studentA.Id,
            IsCompleted = true,
            RequiresManualGrading = false,
            IsPassed = true,
            ScorePercent = 80,
            SubmittedAtUtc = DateTime.UtcNow.AddMinutes(-2)
        });
        await db.SaveChangesAsync();

        var teacherStudents = await dashboard.GetTeacherStudentsAsync(teacher.Id);
        Assert.Equal(2, teacherStudents.Count);
        Assert.Contains(teacherStudents, x => x.FullName == "Ama Savi" && x.CurrentKlasseName == "CP1A");
        Assert.Contains(teacherStudents, x => x.FullName == "Yao Tete" && x.CurrentKlasseName == "CP2A");

        var progress = await dashboard.GetTeacherStudentProgressAsync(teacher.Id);
        Assert.Equal(2, progress.Count);
        var studentAProgress = Assert.Single(progress.Where(x => x.FullName == "Ama Savi"));
        var studentBProgress = Assert.Single(progress.Where(x => x.FullName == "Yao Tete"));
        Assert.Equal(40, studentAProgress.CombinedScorePercent);
        Assert.Equal(2, studentAProgress.CombinedScoreCount);
        Assert.Equal(0, studentAProgress.CompletedLessonsCount);
        Assert.Equal(1, studentBProgress.CombinedScoreCount);
        Assert.Equal(100, studentBProgress.CombinedScorePercent);

        var studentLessons = await learning.GetLessonsForStudentAsync(studentA.Id);
        var visibleLesson = Assert.Single(studentLessons);
        Assert.Equal("Alphabet et salutations", visibleLesson.Title);

        var exerciseResult = await learning.SubmitExerciseAsync(exerciseA1.Id, studentA.Id, new ExerciseSubmission
        {
            SelectedOptionIds = [correctA1.Id]
        });
        Assert.True(exerciseResult.Succeeded);
        Assert.True(exerciseResult.IsCorrect);

        var studentDashboard = await dashboard.GetStudentDashboardAsync(studentA.Id);
        Assert.NotNull(studentDashboard);
        Assert.Equal("École Lumière", studentDashboard!.SchoolName);
        Assert.True(studentDashboard.HasPaidHalloAppAbo);
        Assert.True(studentDashboard.HasActiveSubscription);
        Assert.True(studentDashboard.CanTakeFinalExam);
        Assert.Equal(0, studentDashboard.CompletedLessonsCount);
        Assert.True(studentDashboard.CombinedScorePercent >= 60);
        Assert.Contains(studentDashboard.CourseProgress, x => x.CourseTitle == "CP1A" && x.IsUnlocked);
        Assert.Contains(studentDashboard.CourseProgress, x => x.CourseTitle == "CP2A" && !x.IsCurrentLevel);

        // --- Saison-Workflow: Finanzen, Schulgebühren, Schließung, Wiedereröffnung ---
        var aboService = new AboService(db);
        var finanzService = new FinanzService(db);
        var examService = new ExamService(db);

        var (cp1RunStartOk, cp1RunStartError, cp1Run) = await finanzService.DurchlaufStartenAsync(
            school.Id,
            klasseCp1.CourseId.Value,
            12500m,
            DateTime.UtcNow.AddDays(-30),
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp1RunStartOk, cp1RunStartError);
        Assert.NotNull(cp1Run);

        var (cp2RunStartOk, cp2RunStartError, cp2Run) = await finanzService.DurchlaufStartenAsync(
            school.Id,
            klasseCp2.CourseId.Value,
            13000m,
            DateTime.UtcNow.AddDays(-30),
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp2RunStartOk, cp2RunStartError);
        Assert.NotNull(cp2Run);

        var (cp1FeePaymentOk, cp1FeePaymentError, cp1FeePayment) = await finanzService.ZahlungBuchenAsync(
            cp1Run!.Id,
            studentA.Id,
            8000m,
            ZahlungsArt.Rate,
            DateTime.UtcNow.AddDays(-20),
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp1FeePaymentOk, cp1FeePaymentError);
        Assert.NotNull(cp1FeePayment);

        var (cp2FeePaymentOk, cp2FeePaymentError, cp2FeePayment) = await finanzService.ZahlungBuchenAsync(
            cp2Run!.Id,
            studentB.Id,
            13000m,
            ZahlungsArt.Cash,
            DateTime.UtcNow.AddDays(-19),
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp2FeePaymentOk, cp2FeePaymentError);
        Assert.NotNull(cp2FeePayment);

        var (studentBAboPaidOk, studentBAboPaidError) = await aboService.BezahltSetzenAsync(
            studentB.Id,
            klasseCp2.CourseId.Value,
            director.Id);
        Assert.True(studentBAboPaidOk, studentBAboPaidError);

        var cp1ReportBeforeClose = await finanzService.GetKlassenFinanzberichtAsync(cp1Run.Id);
        Assert.NotNull(cp1ReportBeforeClose);
        Assert.Equal(1, cp1ReportBeforeClose!.SchuelerAnzahl);
        Assert.Equal(12500m, cp1ReportBeforeClose.SchulgebuehrSoll);
        Assert.Equal(8000m, cp1ReportBeforeClose.SchulgebuehrBezahlt);
        Assert.Equal(4500m, cp1ReportBeforeClose.SchulgebuehrOffen);

        var cp2ReportBeforeClose = await finanzService.GetKlassenFinanzberichtAsync(cp2Run.Id);
        Assert.NotNull(cp2ReportBeforeClose);
        Assert.Equal(1, cp2ReportBeforeClose!.SchuelerAnzahl);
        Assert.Equal(13000m, cp2ReportBeforeClose.SchulgebuehrSoll);
        Assert.Equal(13000m, cp2ReportBeforeClose.SchulgebuehrBezahlt);
        Assert.Equal(0m, cp2ReportBeforeClose.SchulgebuehrOffen);

        var (cp1DecisionOk, cp1DecisionMessage) = await examService.SetKlassenBestandenEntscheidungAsync(
            cp1Run.Id,
            studentA.Id,
            isBestanden: true,
            entscheidetVonUserId: director.Id);
        Assert.True(cp1DecisionOk, cp1DecisionMessage);

        var (cp2DecisionOk, cp2DecisionMessage) = await examService.SetKlassenBestandenEntscheidungAsync(
            cp2Run.Id,
            studentB.Id,
            isBestanden: true,
            entscheidetVonUserId: director.Id);
        Assert.True(cp2DecisionOk, cp2DecisionMessage);

        var (cp1CloseOk, cp1CloseError, cp1OpenDebt) = await finanzService.DurchlaufBeendenAsync(
            cp1Run.Id,
            DateTime.UtcNow.AddDays(-1),
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp1CloseOk, cp1CloseError);
        Assert.Equal(4500m, cp1OpenDebt);

        var (cp2CloseOk, cp2CloseError, cp2OpenDebt) = await finanzService.DurchlaufBeendenAsync(
            cp2Run.Id,
            DateTime.UtcNow.AddDays(-1),
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp2CloseOk, cp2CloseError);
        Assert.Equal(0m, cp2OpenDebt);

        var (paymentAfterCloseOk, paymentAfterCloseError, _) = await finanzService.ZahlungBuchenAsync(
            cp1Run.Id,
            studentA.Id,
            1000m,
            ZahlungsArt.Rate,
            DateTime.UtcNow,
            director.Id,
            autorisierungErzwingen: true);
        Assert.False(paymentAfterCloseOk);
        Assert.Contains("clôturé", paymentAfterCloseError);

        var reopenedCp1PriceProposal = await finanzService.GetPreisVorschlagAsync(school.Id, klasseCp1.CourseId.Value);
        var reopenedCp2PriceProposal = await finanzService.GetPreisVorschlagAsync(school.Id, klasseCp2.CourseId.Value);
        Assert.Equal(12500m, reopenedCp1PriceProposal);
        Assert.Equal(13000m, reopenedCp2PriceProposal);

        var (cp1ReopenOk, cp1ReopenError, cp1ReopenedRun) = await finanzService.DurchlaufStartenAsync(
            school.Id,
            klasseCp1.CourseId.Value,
            reopenedCp1PriceProposal,
            DateTime.UtcNow,
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp1ReopenOk, cp1ReopenError);
        Assert.NotNull(cp1ReopenedRun);
        Assert.NotEqual(cp1Run.Id, cp1ReopenedRun!.Id);

        var (cp2ReopenOk, cp2ReopenError, cp2ReopenedRun) = await finanzService.DurchlaufStartenAsync(
            school.Id,
            klasseCp2.CourseId.Value,
            reopenedCp2PriceProposal,
            DateTime.UtcNow,
            director.Id,
            autorisierungErzwingen: true);
        Assert.True(cp2ReopenOk, cp2ReopenError);
        Assert.NotNull(cp2ReopenedRun);
        Assert.NotEqual(cp2Run.Id, cp2ReopenedRun!.Id);

        var activeCp1Run = await finanzService.GetAktivenDurchlaufAsync(school.Id, klasseCp1.CourseId.Value);
        var activeCp2Run = await finanzService.GetAktivenDurchlaufAsync(school.Id, klasseCp2.CourseId.Value);
        Assert.NotNull(activeCp1Run);
        Assert.NotNull(activeCp2Run);
        Assert.Equal(cp1ReopenedRun.Id, activeCp1Run!.Id);
        Assert.Equal(cp2ReopenedRun.Id, activeCp2Run!.Id);

        Assert.Equal(4, await db.KursNiveauDurchlaeufe.CountAsync());
        Assert.Equal(2, await db.KursNiveauDurchlaeufe.CountAsync(run => run.EndDatum != null));
        Assert.Equal(2, await db.KursNiveauDurchlaeufe.CountAsync(run => run.EndDatum == null));

        var lossCategoryIds = await db.FinanzKategorien
            .Where(category => category.SystemCode == FinanzSystemCode.SchulgebuehrVerlust)
            .Select(category => category.Id)
            .ToListAsync();
        var lossBooking = Assert.Single(db.FinanzBuchungen.Where(x => lossCategoryIds.Contains(x.KategorieID)));
        Assert.Equal(4500m, lossBooking.Betrag);
    }

    [Fact]
    public async Task VollstaendigerAblauf_VonSchulleiterBisSchueler_FunktioniertDurchgehend()
    {
        // ============================================================
        // Arrange: Dienste und In-Memory-Datenbank vorbereiten
        // ============================================================
        using var db = TestDbContext.CreateInMemory();

        // --- 1) Schulleiter registriert eine neue Schule -------------
        var schule = new School
        {
            Id = Guid.NewGuid(),
            Name = "École Hallo Yaoundé",
            City = "Yaoundé",
            IsActive = true,
            ErstelltAmUtc = DateTime.UtcNow.AddMonths(-2),
            PreisHalloBasis = 5000m,
            PreisHalloPlus = 8000m
        };

        var schulleiter = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Directeur Mbarga",
            UserName = "directeur",
            Email = "directeur@hallo.cm"
        };

        // --- 2) Lehrer wird der Schule zugeordnet --------------------
        var lehrer = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Enseignante Ngo",
            UserName = "enseignante",
            Email = "enseignante@hallo.cm"
        };

        var lehrerSchule = new UserSchool
        {
            AppUserId = lehrer.Id,
            SchoolId = schule.Id,
            CanManageStudents = true
        };

        // --- Kursniveau, das später freigeschaltet wird --------------
        var niveau = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Débutant", SchoolId = schule.Id, IstSystemNiveau = true };

        // --- Lektion mit einer Übung im Niveau A1.1 -----------------
        var lektion = new Lesson { Id = Guid.NewGuid(), KlasseId = niveau.Id, Title = "Salutations", Order = 1 };
        var uebungRichtig = new ExerciseOption { Id = Guid.NewGuid(), Text = "Guten Tag", IsCorrect = true, Order = 1 };
        var uebungFalsch = new ExerciseOption { Id = Guid.NewGuid(), Text = "Gute Nacht", IsCorrect = false, Order = 2 };
        var uebung = new Exercise
        {
            Id = Guid.NewGuid(),
            LessonId = lektion.Id,
            Title = "Begrüßung",
            Question = "Was heißt 'Bonjour'?",
            Order = 1,
            Options = { uebungRichtig, uebungFalsch }
        };

        // --- Prüfung im Niveau A1.1 ---------------------------------
        var pruefungRichtig = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "Guten Tag", IsCorrect = true, Order = 1 };
        var pruefungFalsch = new ExamQuestionOption { Id = Guid.NewGuid(), Text = "Gute Nacht", IsCorrect = false, Order = 2 };
        var pruefungsFrage = new ExamQuestion
        {
            Id = Guid.NewGuid(),
            Prompt = "Was heißt 'Bonjour'?",
            Order = 1,
            Options = { pruefungRichtig, pruefungFalsch }
        };
        var pruefung = new Exam
        {
            Id = Guid.NewGuid(),
            KlasseId = niveau.Id,
            Title = "Examen A1.1",
            Description = "Vocabulaire de base",
            DurationInMinutes = 30,
            PassingScore = 50,
            Questions = { pruefungsFrage },
            IsPublishedToStudents = true
        };

        // --- 3) Schüler registriert sich selbst (zunächst blockiert) -
        var schuelerUser = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Élève Fotso",
            UserName = "eleve",
            Email = "eleve@hallo.cm",
            IsSelfRegistered = true,
            IsBlocked = true,
            GeburtsdatumUtc = DateTime.UtcNow.AddYears(-15)
        };

        var schuelerProfil = new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = schuelerUser.Id,
            SchoolId = schule.Id,
            CurrentLevelCode = "A1.1",
            CurrentKlasseId = niveau.Id
        };

        db.Schools.Add(schule);
        db.Users.Add(schulleiter);
        db.Users.Add(lehrer);
        db.Users.Add(schuelerUser);
        db.UserSchools.Add(lehrerSchule);
        db.Klassen.Add(niveau);
        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung
        {
            Id = Guid.NewGuid(),
            LehrerID = lehrer.Id,
            SchulID = schule.Id,
            KlasseId = niveau.Id
        });
        db.Lessons.Add(lektion);
        db.Exercises.Add(uebung);
        db.Exams.Add(pruefung);
        db.StudentProfiles.Add(schuelerProfil);
        db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = schule.Id,
            KlasseId = niveau.Id,
            NiveauPreis = 12500m,
            StartDatum = DateTime.UtcNow.AddDays(-1),
            EndDatum = null,
            ErstelltVonUserID = schulleiter.Id
        });
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { schulleiter, lehrer, schuelerUser });
        userManager.SeedRole(schulleiter, "SchulLeiter");
        userManager.SeedRole(lehrer, "Lehrer");
        userManager.SeedRole(schuelerUser, "Schueler");

        var dashboard = new DashboardService(db, userManager, new FakeMediaStorageService());
        var abo = new AboService(db);
        var abrechnung = new AbrechnungService(db);
        var learning = new LearningService(db);
        var exam = new ExamService(db);

        // ============================================================
        // Act & Assert: Ablauf Schritt für Schritt prüfen
        // ============================================================

        // --- Schritt A: Lehrer sieht seine Schule + offenen Schüler --
        var lehrerSchulen = await dashboard.GetTeacherSchoolsAsync(lehrer.Id);
        var meineSchule = Assert.Single(lehrerSchulen);
        Assert.Equal("École Hallo Yaoundé", meineSchule.SchoolName);
        Assert.Equal(1, meineSchule.StudentCount);
        Assert.Equal(1, meineSchule.PendingApprovalCount);

        var offeneSchueler = await dashboard.GetTeacherStudentsAsync(lehrer.Id);
        var offenerSchueler = Assert.Single(offeneSchueler);
        Assert.True(offenerSchueler.IsBlocked);
        Assert.True(offenerSchueler.IsSelfRegistered);

        // --- Schritt B: Lehrer/Schulleiter validiert den Schüler -----
        await dashboard.SetStudentBlockedStateAsync(schuelerUser.Id, false);
        var nachValidierung = await dashboard.GetStudentDashboardAsync(schuelerUser.Id);
        Assert.NotNull(nachValidierung);
        Assert.False(nachValidierung!.IsBlocked);

        // --- Schritt C: Abo-Zuweisung mit schulspezifischem Preis ----
        var (aboOk, _) = await abo.AboZuweisenAsync(schuelerUser.Id, niveau.Id, AboTyp.HalloApp);
        Assert.True(aboOk);
        var freischaltung = Assert.Single(db.LevelEnrollments);
        Assert.True(freischaltung.IsUnlocked);
        Assert.True(freischaltung.IsPaid);
        Assert.NotNull(freischaltung.PaidAtUtc);
        Assert.Equal(AboTyp.HalloApp, freischaltung.AboTyp);
        Assert.Equal(8000m, freischaltung.PriceAtEnrollment); // eingefrorener Schulpreis

        // --- Schritt D: Zahlung ist bereits im 1‑Schritt-Flow gesetzt --
        Assert.True(db.LevelEnrollments.Single().IsPaid);

        // --- Schritt E: Offene Abrechnungsübersicht ------------------
        var uebersicht = await abrechnung.GetOffeneUebersichtAsync(schule.Id);
        Assert.Equal(1, uebersicht.AnzahlHalloBasis);
        Assert.Equal(0, uebersicht.AnzahlHalloPlus);
        Assert.Equal(
            freischaltung.PriceAtEnrollment
                - freischaltung.SchoolLeaderCommissionAtEnrollment
                - freischaltung.TeacherCommissionAtEnrollment,
            uebersicht.OffenerUmsatz);

        // --- Schritt F: Abrechnung erstellen, akzeptieren und bestätigen ---
        var (erstelltOk, _) = await abrechnung.AbrechnungErstellenAsync(schule.Id);
        Assert.True(erstelltOk);
        var periode = Assert.Single(db.Abrechnungsperioden);
        Assert.Equal(periode.Id, db.LevelEnrollments.Single().AbgerechnetInPeriodeId);

        var (akzeptiertOk, _) = await abrechnung.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: true);
        Assert.True(akzeptiertOk);
        var (bestaetigtOk, _) = await abrechnung.AdminBestaetigenAsync(periode.Id, await TestIdentityFactory.AddSuperAdminAsync(db));
        Assert.True(bestaetigtOk);
        var periodeNachBestaetigung = db.Abrechnungsperioden.Single();
        Assert.True(periodeNachBestaetigung.IstAbgeschlossen);
        Assert.Equal(uebersicht.OffenerUmsatz, periodeNachBestaetigung.Umsatz);

        // --- Schritt F2: Provisionsverteilung der abgeschlossenen Periode ---
        var provision = new ProvisionsService(db);
        var provisionsUebersicht = await provision.GetPeriodeUebersichtAsync(periodeNachBestaetigung.Id);
        Assert.Equal(1, provisionsUebersicht.AnzahlPositionen);
        Assert.Equal(320m, provisionsUebersicht.ProvisionSchulleiter); // 4 % von 8.000 FCFA
        Assert.Equal(640m, provisionsUebersicht.ProvisionLehrer); // 8 % von 8.000 FCFA
        Assert.Equal(
            8000m - 320m - 640m,
            provisionsUebersicht.ProvisionAdmin);
        Assert.Equal(8000m, provisionsUebersicht.Gesamtumsatz);

        // --- Schritt G: Abgerechnete Zahlung ist unumkehrbar ---------
        var (ruecknahmeOk, ruecknahmeMsg) = await abo.BezahltZuruecknehmenAsync(schuelerUser.Id, niveau.Id, istSuperAdmin: true);
        Assert.False(ruecknahmeOk);
        Assert.Contains("facturé", ruecknahmeMsg);

        // --- Schritt H: Schüler-Dashboard zeigt freigeschaltetes Niveau
        var schuelerDashboard = await dashboard.GetStudentDashboardAsync(schuelerUser.Id);
        Assert.NotNull(schuelerDashboard);
        Assert.Equal("Élève Fotso", schuelerDashboard!.FullName);
        Assert.Equal("École Hallo Yaoundé", schuelerDashboard.SchoolName);
        Assert.False(schuelerDashboard.IsBlocked);
        var kurs = Assert.Single(schuelerDashboard.CourseProgress, x => x.CourseTitle == "Débutant");
        Assert.True(kurs.IsUnlocked);
        Assert.True(kurs.IsCurrentLevel);

        // --- Schritt I: Abrechnung ist nun leer, Historie gefüllt ----
        var uebersichtNachAbschluss = await abrechnung.GetOffeneUebersichtAsync(schule.Id);
        Assert.Equal(0, uebersichtNachAbschluss.AnzahlPositionen);

        var historie = await abrechnung.GetHistorieAsync(schule.Id);
        var historienEintrag = Assert.Single(historie);
        Assert.Equal(uebersicht.OffenerUmsatz, historienEintrag.Umsatz);

        // --- Schritt J: Schüler sieht freigeschaltete Lektion --------
        var lektionen = await learning.GetLessonsForStudentAsync(schuelerUser.Id);
        var sichtbareLektion = Assert.Single(lektionen);
        Assert.Equal("Salutations", sichtbareLektion.Title);

        // --- Schritt K: Schüler löst die Übung korrekt ---------------
        var uebungsErgebnis = await learning.SubmitAnswerAsync(uebung.Id, uebungRichtig.Id, schuelerUser.Id);
        Assert.True(uebungsErgebnis.Succeeded);
        Assert.True(uebungsErgebnis.IsCorrect);
        Assert.True(uebungsErgebnis.LessonCompleted);

        // --- Schritt L: Schüler startet und besteht die Prüfung ------
        var pruefungsSession = await exam.StartExamAsync(pruefung.Id, schuelerUser.Id);
        Assert.NotNull(pruefungsSession);
        Assert.Equal("Examen A1.1", pruefungsSession!.Title);
        var frageId = pruefungsSession.Questions[0].QuestionId;
        var antworten = new Dictionary<Guid, Guid> { [frageId] = pruefungRichtig.Id };

        var pruefungsErgebnis = await exam.SubmitExamAsync(pruefungsSession.AttemptId, schuelerUser.Id, antworten);
        Assert.True(pruefungsErgebnis.Succeeded);
        Assert.True(pruefungsErgebnis.IsPassed);
        Assert.Equal(100, pruefungsErgebnis.ScorePercent);
    }

    /// <summary>
    /// Prüft die Lehrer-Niveau-Zuordnung: Ohne Zuordnung sieht der Lehrer keine Schüler.
    /// Nach einer Zuordnung durch den Direktor auf ein bestimmtes Niveau sieht er nur
    /// die Schüler dieses Niveaus. Wird die Zuordnung entfernt, sieht er keine Schüler.
    /// </summary>
    [Fact]
    public async Task LehrerNiveauZuordnung_SchraenktSichtbareSchueler_KorrektEin()
    {
        // ============================================================
        // Arrange
        // ============================================================
        using var db = TestDbContext.CreateInMemory();

        var schule = new School
        {
            Id = Guid.NewGuid(),
            Name = "École Hallo Douala",
            City = "Douala",
            IsActive = true
        };

        var direktor = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Directeur Kamga",
            UserName = "directeur2",
            Email = "directeur2@hallo.cm"
        };

        var lehrer = new AppUser
        {
            Id = Guid.NewGuid(),
            FullName = "Enseignant Etoa",
            UserName = "enseignant2",
            Email = "enseignant2@hallo.cm"
        };

        // Direktor und Lehrer sind beide der Schule zugeordnet.
        var direktorSchule = new UserSchool { AppUserId = direktor.Id, SchoolId = schule.Id, CanManageStudents = true };
        var lehrerSchule = new UserSchool { AppUserId = lehrer.Id, SchoolId = schule.Id, CanManageStudents = true };

        var niveauA = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "A1.1", Title = "Débutant" };
        var niveauB = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "A2.1", Title = "Élémentaire" };

        // Zwei Schüler in unterschiedlichen Niveaus derselben Schule.
        var schuelerA = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Nkolo", UserName = "eleveA", Email = "eleveA@hallo.cm" };
        var schuelerB = new AppUser { Id = Guid.NewGuid(), FullName = "Élève Bello", UserName = "eleveB", Email = "eleveB@hallo.cm" };

        var profilA = new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerA.Id, SchoolId = schule.Id, CurrentLevelCode = "A1.1", CurrentKlasseId = niveauA.Id };
        var profilB = new StudentProfile { Id = Guid.NewGuid(), AppUserId = schuelerB.Id, SchoolId = schule.Id, CurrentLevelCode = "A2.1", CurrentKlasseId = niveauB.Id };

        db.Schools.Add(schule);
        db.Users.AddRange(direktor, lehrer, schuelerA, schuelerB);
        db.UserSchools.AddRange(direktorSchule, lehrerSchule);
        db.Klassen.AddRange(niveauA, niveauB);
        db.StudentProfiles.AddRange(profilA, profilB);
        await db.SaveChangesAsync();

        var userManager = TestUserManager.Create(new[] { direktor, lehrer, schuelerA, schuelerB });
        userManager.SeedRole(direktor, "SchulLeiter");
        userManager.SeedRole(lehrer, "Lehrer");
        userManager.SeedRole(schuelerA, "Schueler");
        userManager.SeedRole(schuelerB, "Schueler");

        var dashboard = new DashboardService(db, userManager, new FakeMediaStorageService());

        // ============================================================
        // Act & Assert
        // ============================================================

        // --- Schritt A: Ohne Zuordnung sieht der Lehrer keine Schüler ---
        var alleSchueler = await dashboard.GetTeacherStudentsAsync(lehrer.Id);
        Assert.Empty(alleSchueler);

        // --- Schritt B: Verfügbare Zuordnungen des Direktors abrufen ----
        var moeglicheZuordnungen = await dashboard.GetLehrerNiveauZuordnungenAsync(direktor.Id, lehrer.Id);
        Assert.Equal(2, moeglicheZuordnungen.Count); // A1.1 und A2.1 in der Schule
        Assert.All(moeglicheZuordnungen, z => Assert.False(z.IstZugeordnet));

        // --- Schritt C: Direktor ordnet den Lehrer nur Niveau A1.1 zu ---
        await dashboard.SetLehrerNiveauZuordnungenAsync(
            direktor.Id,
            lehrer.Id,
            new[] { (schule.Id, niveauA.Id, 1m) });

        var nachZuordnung = await dashboard.GetTeacherStudentsAsync(lehrer.Id);
        var sichtbarerSchueler = Assert.Single(nachZuordnung);
        Assert.Equal("A1.1", sichtbarerSchueler.CurrentLevelCode);
        Assert.Equal("Élève Nkolo", sichtbarerSchueler.FullName);

        // Die aktive Zuordnung wird korrekt als gesetzt gemeldet.
        var aktualisierteZuordnungen = await dashboard.GetLehrerNiveauZuordnungenAsync(direktor.Id, lehrer.Id);
        Assert.Contains(aktualisierteZuordnungen, z => z.NiveauCode == "A1.1" && z.IstZugeordnet);
        Assert.Contains(aktualisierteZuordnungen, z => z.NiveauCode == "A2.1" && !z.IstZugeordnet);

        // --- Schritt D: Zuordnung entfernen -> keine Schüler ------------
        await dashboard.SetLehrerNiveauZuordnungenAsync(
            direktor.Id,
            lehrer.Id,
            Array.Empty<(Guid, Guid, decimal)>());

        var nachEntfernung = await dashboard.GetTeacherStudentsAsync(lehrer.Id);
        Assert.Empty(nachEntfernung);
    }

    private sealed class TestSignInManager(UserManager<AppUser> userManager)
        : SignInManager<AppUser>(
            userManager,
            new TestHttpContextAccessor(),
            new TestUserClaimsPrincipalFactory(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<AppUser>>.Instance,
            new TestAuthenticationSchemeProvider(),
            new TestUserConfirmation())
    {
        public override Task<SignInResult> CheckPasswordSignInAsync(AppUser user, string password, bool lockoutOnFailure)
            => Task.FromResult(SignInResult.Success);

        public override Task SignInWithClaimsAsync(AppUser user, AuthenticationProperties? authenticationProperties, IEnumerable<System.Security.Claims.Claim> additionalClaims)
            => Task.CompletedTask;

        public override Task SignOutAsync()
            => Task.CompletedTask;
    }

    private sealed class TestHttpContextAccessor : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; } = new Microsoft.AspNetCore.Http.DefaultHttpContext();
    }

    private sealed class TestUserClaimsPrincipalFactory()
        : IUserClaimsPrincipalFactory<AppUser>
    {
        public Task<System.Security.Claims.ClaimsPrincipal> CreateAsync(AppUser user)
            => Task.FromResult(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("Test")));
    }

    private sealed class TestAuthenticationSchemeProvider()
        : IAuthenticationSchemeProvider
    {
        public Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync()
            => Task.FromResult<IEnumerable<AuthenticationScheme>>([]);

        public Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
            => Task.FromResult<AuthenticationScheme?>(null);

        public Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
            => Task.FromResult<AuthenticationScheme?>(null);

        public Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
            => Task.FromResult<AuthenticationScheme?>(null);

        public Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync()
            => Task.FromResult<AuthenticationScheme?>(null);

        public Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
            => Task.FromResult<AuthenticationScheme?>(null);

        public Task<AuthenticationScheme?> GetSchemeAsync(string name)
            => Task.FromResult<AuthenticationScheme?>(null);

        public void AddScheme(AuthenticationScheme scheme)
        {
        }

        public void RemoveScheme(string name)
        {
        }

        public Task<AuthenticationScheme?> GetRequestHandlerSchemeAsync()
            => Task.FromResult<AuthenticationScheme?>(null);

        public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync()
            => Task.FromResult<IEnumerable<AuthenticationScheme>>([]);
    }

    private sealed class TestUserConfirmation : IUserConfirmation<AppUser>
    {
        public Task<bool> IsConfirmedAsync(UserManager<AppUser> manager, AppUser user)
            => Task.FromResult(true);
    }
}
