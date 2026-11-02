using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class PromotionTeilnahmeTests
{
    [Fact]
    public async Task VersetzungErfasstLaufendenZielzyklusZumEingefrorenenPreisOhneZahlungsfiktion()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = new School { Id = Guid.NewGuid(), Name = "École", SubscriptionPriceOverride = 9900m };
        var old = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CM1" };
        var target = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CM2" };
        var student = new AppUser { Id = Guid.NewGuid() };
        var sourceCycle = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = old.Id, EndDatum = DateTime.UtcNow, PlattformPreisProSchueler = 2000m };
        var targetCycle = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = target.Id, PlattformPreisProSchueler = 3000m };
        db.Schools.Add(school); db.Klassen.AddRange(old, target); db.Users.Add(student);
        db.StudentProfiles.Add(new StudentProfile { AppUserId = student.Id, SchoolId = school.Id, CurrentKlasseId = old.Id });
        db.KursNiveauDurchlaeufe.AddRange(sourceCycle, targetCycle);
        db.KlassenAbschlussEntscheidungen.Add(new KlassenAbschlussEntscheidung { DurchlaufId = sourceCycle.Id, StudentId = student.Id, IsBestanden = true });
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = old.Id, DurchlaufId = sourceCycle.Id, IstAktuell = false, PriceAtEnrollment = 2000m });
        await db.SaveChangesAsync();
        var service = new ExamService(db);
        Assert.True((await service.VersetzeSchuelerAsync(sourceCycle.Id, [student.Id], target.Id)).Succeeded);
        Assert.True((await service.VersetzeSchuelerAsync(sourceCycle.Id, [student.Id], target.Id)).Succeeded);
        var position = Assert.Single(db.LevelEnrollments.Where(x => x.DurchlaufId == targetCycle.Id));
        Assert.Equal(3000m, position.PriceAtEnrollment); Assert.False(position.IsPaid); Assert.Null(position.PaidAtUtc);
        Assert.Equal(0m, position.SchoolLeaderCommissionAtEnrollment); Assert.Equal(0m, position.TeacherCommissionAtEnrollment);
        Assert.Equal(2, db.LevelEnrollments.Count()); Assert.Empty(db.FinanzBuchungen);
        Assert.Equal(target.Id, db.StudentProfiles.Single().CurrentKlasseId);
    }
}
