using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Admin;
using SkoulouApp.Core.Models.TeacherLessons;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Data;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Opt-in: SKOULOU_SQL_TESTS=1. Prüft Berechtigung und Kennzeichnung « Saisi par la direction »
/// gegen echten SQL Server (Übersetzung der Abfragen, Fremdschlüssel ErfasstVonId).
/// Ausschließlich zufällig benannte lokale Testdatenbank.
/// </summary>
public class DirektionsErfassungSqlTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task SqlServer_DirektionErfasstImNamenDesLehrers()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        var name = "SKOULOUAPP_TEST_" + Guid.NewGuid().ToString("N");
        var factory = new SqlFactory(name);
        try
        {
            await using (var schema = factory.CreateDbContext()) await schema.Database.MigrateAsync();
            var schule = Guid.NewGuid(); var andereSchule = Guid.NewGuid(); var klasse = Guid.NewGuid();
            var leiter = Guid.NewGuid(); var lehrer = Guid.NewGuid(); var fremderLeiter = Guid.NewGuid();
            await using (var db = factory.CreateDbContext())
            {
                db.Schools.AddRange(new School { Id = schule, Name = "SQL-Direction A" }, new School { Id = andereSchule, Name = "SQL-Direction B" });
                db.Users.AddRange(
                    new AppUser { Id = leiter, Email = "leiter@sql.test", FullName = "Direction A" },
                    new AppUser { Id = lehrer, Email = "lehrer@sql.test", FullName = "Enseignant A" },
                    new AppUser { Id = fremderLeiter, Email = "fremd@sql.test", FullName = "Direction B" });
                var leiterRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
                var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer };
                db.Roles.AddRange(leiterRolle, lehrerRolle);
                db.UserRoles.AddRange(
                    new AppUserRole { UserId = leiter, RoleId = leiterRolle.Id },
                    new AppUserRole { UserId = fremderLeiter, RoleId = leiterRolle.Id },
                    new AppUserRole { UserId = lehrer, RoleId = lehrerRolle.Id });
                db.UserSchools.AddRange(
                    new UserSchool { AppUserId = leiter, SchoolId = schule },
                    new UserSchool { AppUserId = lehrer, SchoolId = schule },
                    new UserSchool { AppUserId = fremderLeiter, SchoolId = andereSchule });
                db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "CM1", Title = "CM1" });
                db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = lehrer, SchulID = schule, KlasseId = klasse });
                await db.SaveChangesAsync();
            }

            await using (var db = factory.CreateDbContext())
            {
                var erfassung = new DirektionsErfassungService(db);
                Assert.True(await erfassung.DarfFuerLehrerHandelnAsync(leiter, lehrer));
                Assert.False(await erfassung.DarfFuerLehrerHandelnAsync(fremderLeiter, lehrer));
                Assert.False(await erfassung.DarfFuerLehrerHandelnAsync(lehrer, leiter));
                Assert.Equal(lehrer, Assert.Single(await erfassung.GetLehrerFuerDirektionAsync(leiter)).Id);

                var lessonId = await new TeacherLessonService(db).SaveLessonAsync(new TeacherLessonFormModel
                {
                    KlasseId = klasse,
                    Title = "Les fractions",
                    Pages = [new TeacherLessonPageFormModel { ContentHtml = "<p>Contenu</p>" }]
                }, lehrer, handelnderId: leiter);

                var examId = await new ExamService(db).SaveExamAsync(lehrer, new ExamFormModel
                {
                    Title = "Évaluation", NiveauCode = "CM1", KlasseId = klasse,
                    Exercises = [new ExamExerciseFormModel { Questions = [new ExamQuestionFormModel { Prompt = "1 + 1 ?", Options = [new() { Text = "2", IsCorrect = true }, new() { Text = "3" }] }] }]
                }, handelnderId: leiter);

                var lesson = await db.TeacherLessons.AsNoTracking().SingleAsync(x => x.Id == lessonId);
                Assert.Equal(lehrer, lesson.AuthorId);
                Assert.Equal(leiter, lesson.ErfasstVonId);
                var exam = await db.Exams.AsNoTracking().SingleAsync(x => x.Id == examId);
                Assert.Equal(lehrer, exam.AuthorId);
                Assert.Equal(leiter, exam.ErfasstVonId);
                Assert.True(Assert.Single(await new ExamService(db).GetTeacherExamsAsync(lehrer)).IstVonDirektionErfasst);
                Assert.NotNull(await new ExamService(db).GetExamForPdfAsync(leiter, examId));
                Assert.Null(await new ExamService(db).GetExamForPdfAsync(fremderLeiter, examId));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ExamService(db).SaveExamAsync(lehrer, new ExamFormModel
                {
                    Title = "Refus", NiveauCode = "CM1", KlasseId = klasse,
                    Exercises = [new ExamExerciseFormModel { Questions = [new ExamQuestionFormModel { Prompt = "?", Options = [new() { Text = "a", IsCorrect = true }, new() { Text = "b" }] }] }]
                }, handelnderId: fremderLeiter));
                Assert.Equal(1, await db.Exams.CountAsync());
                Assert.False(await db.StudentProfiles.AnyAsync(x => x.AntwortTonAus));
            }
        }
        finally
        {
            // Niemals eine konfigurierbare Produktiv-/Entwicklungsdatenbank löschen.
            if (!name.StartsWith("SKOULOUAPP_TEST_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database name.");
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class SqlFactory(string name) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Encrypt=True", sql => sql.EnableRetryOnFailure()).Options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
