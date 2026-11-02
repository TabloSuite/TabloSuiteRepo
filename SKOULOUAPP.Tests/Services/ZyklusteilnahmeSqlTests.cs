using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.Services;
using SkoulouApp.WebApp.Data;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class ZyklusteilnahmeSqlTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task SqlServer_TeilnahmeStornoUndAbschlussBleibenKonsistent()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        var name = "SKOULOUAPP_TEST_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Encrypt=True",
                sql => sql.EnableRetryOnFailure()).Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            var schule = new School { Id = Guid.NewGuid(), Name = "SQL-Test", SubscriptionPriceOverride = 3000 };
            var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
            var admin = new AppUser { Id = Guid.NewGuid(), FullName = "Administration", UserName = "admin-sql", Email = "admin@sql.test" };
            var student = new AppUser { Id = Guid.NewGuid(), FullName = "Élève", UserName = "student-sql", Email = "student@sql.test" };
            var fehler = new AppUser { Id = Guid.NewGuid(), FullName = "Erreur", UserName = "error-sql", Email = "error@sql.test" };
            var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            db.Schools.Add(schule); db.Klassen.Add(klasse); db.Users.AddRange(admin, student, fehler);
            db.Roles.Add(rolle); db.UserRoles.Add(new AppUserRole { RoleId = rolle.Id, UserId = admin.Id });
            db.StudentProfiles.AddRange(
                new StudentProfile { SchoolId = schule.Id, AppUserId = student.Id, CurrentKlasseId = klasse.Id },
                new StudentProfile { SchoolId = schule.Id, AppUserId = fehler.Id, CurrentKlasseId = klasse.Id });
            await db.SaveChangesAsync();
            var finanz = new FinanzService(db);
            var starts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var parallelDb = new ApplicationDbContext(options);
                return await new FinanzService(parallelDb).DurchlaufStartenAsync(schule.Id, klasse.Id, 1000, DateTime.Today.AddDays(-10), admin.Id);
            }));
            Assert.Single(starts, x => x.Succeeded);
            var zyklus = Assert.Single(await db.KursNiveauDurchlaeufe.ToListAsync());
            var vorlaeufig = await new AbrechnungService(db).GetZyklusVerpflichtungenAsync(schule.Id, admin.Id);
            Assert.Equal(6000m, vorlaeufig.VorlaeufigerBetrag);
            Assert.Equal(2, vorlaeufig.TeilnehmerAnzahl);
            var spaeter = new AppUser { Id = Guid.NewGuid(), FullName = "Arrivée tardive", UserName = "late-sql", Email = "late@sql.test" };
            db.Users.Add(spaeter);
            db.StudentProfiles.Add(new StudentProfile { SchoolId = schule.Id, AppUserId = spaeter.Id, CurrentKlasseId = klasse.Id });
            await db.SaveChangesAsync();
            // Unabhängige Contexts wie bei zwei parallelen Blazor-Aktionen.
            await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                await using var parallelDb = new ApplicationDbContext(options);
                await new ZyklusTeilnahmeService(parallelDb).AktiveKlasseErfassenAsync(schule.Id, klasse.Id);
            }));
            Assert.Equal(3, await db.LevelEnrollments.CountAsync());
            var falsch = await db.LevelEnrollments.SingleAsync(x => x.StudentId == fehler.Id);
            await new ZyklusTeilnahmeService(db).StornierenAsync(admin.Id, falsch.Id, "Affectation erronée");
            await new ZyklusTeilnahmeService(db).AktiveKlasseErfassenAsync(schule.Id, klasse.Id);
            Assert.Equal(3, await db.LevelEnrollments.CountAsync());
            var abschluesse = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
            {
                await using var parallelDb = new ApplicationDbContext(options);
                return await new FinanzService(parallelDb).DurchlaufBeendenAsync(zyklus.Id, DateTime.Today, admin.Id);
            }));
            Assert.Single(abschluesse, x => x.Succeeded);
            db.ChangeTracker.Clear();
            Assert.False((await finanz.DurchlaufBeendenAsync(zyklus.Id, DateTime.Today, admin.Id)).Succeeded);
            var rechnung = Assert.Single(await db.Abrechnungsperioden.ToListAsync());
            Assert.Equal(6000, rechnung.Umsatz); Assert.Equal(2, rechnung.AnzahlAbonnements);
            var fakturiert = await new AbrechnungService(db).GetZyklusVerpflichtungenAsync(schule.Id, admin.Id);
            Assert.Equal(0m, fakturiert.VorlaeufigerBetrag);
            Assert.Equal(6000m, fakturiert.OffeneRechnungenBetrag);
            Assert.Equal(0, (await finanz.GetFinanzAbgleichAsync(schule.Id)).PlattformRechnungenMitAbweichung);
            Assert.Null(falsch.AbgerechnetInPeriodeId); Assert.NotNull(falsch.StorniertAmUtc);
            Assert.All(await db.LevelEnrollments.ToListAsync(), x => Assert.False(x.IsPaid));
            Assert.Equal(3, await db.FinanzBuchungen.CountAsync()); // Zwei Plattformaufwände und bestätigter Schulgebührenverlust.
            var exam = new Exam { Id = Guid.NewGuid(), KlasseId = klasse.Id, AuthorId = admin.Id,
                Title = "Test parallèle", IsPublishedToStudents = true, DurationInMinutes = 30 };
            db.Exams.Add(exam);
            db.ExamQuestions.Add(new ExamQuestion { Id = Guid.NewGuid(), ExamId = exam.Id, Prompt = "Question", Points = 1 });
            await db.SaveChangesAsync();
            var sessions = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
            {
                await using var parallelDb = new ApplicationDbContext(options);
                return await new ExamService(parallelDb).StartExamAsync(exam.Id, student.Id);
            }));
            Assert.All(sessions, session => Assert.NotNull(session));
            Assert.Single(sessions.Select(session => session!.AttemptId).Distinct());
            Assert.Single(await db.ExamAttempts.ToListAsync());
        }
        finally
        {
            if (!name.StartsWith("SKOULOUAPP_TEST_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database name.");
            await db.Database.EnsureDeletedAsync();
        }
    }
}
