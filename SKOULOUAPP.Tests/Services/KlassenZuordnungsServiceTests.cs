using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class KlassenZuordnungsServiceTests
{
    [Fact]
    public async Task ErlaubtNurEigeneSchulklassenUnabhaengigVonBezeichnung()
    {
        using var db = TestDbContext.CreateInMemory();
        var schoolID = Guid.NewGuid(); var studentID = Guid.NewGuid();
        var eigene = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolID, Code = "CM1A" };
        var andere = new Klasse { Id = Guid.NewGuid(), SchoolId = Guid.NewGuid(), Code = "CM1A" };
        var system = new Klasse { Id = Guid.NewGuid(), SchoolId = schoolID, Code = "Archiv", IstSystemNiveau = true };
        var profil = new StudentProfile { AppUserId = studentID, SchoolId = schoolID };
        db.Klassen.AddRange(eigene, andere, system); db.StudentProfiles.Add(profil); await db.SaveChangesAsync();
        var service = new KlassenZuordnungsService(db);
        Assert.True(await service.IstKlasseErlaubtAsync(studentID, eigene.Id));
        Assert.False(await service.IstKlasseErlaubtAsync(studentID, andere.Id));
        Assert.False(await service.IstKlasseErlaubtAsync(studentID, system.Id));
        Assert.False(await service.IstKlasseErlaubtAsync(Guid.NewGuid(), eigene.Id));
        profil.IsArchived = true; await db.SaveChangesAsync();
        Assert.False(await service.IstKlasseErlaubtAsync(studentID, eigene.Id));
    }
}
