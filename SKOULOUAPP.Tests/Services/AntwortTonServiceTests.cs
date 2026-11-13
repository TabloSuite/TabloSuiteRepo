using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Gesprochene Rückmeldung: Schalter pro Schülerkonto (gilt auf jedem Gerät).</summary>
public class AntwortTonServiceTests
{
    [Fact]
    public async Task Schalter_wird_am_Schuelerkonto_gespeichert()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École" };
        var schueler = new AppUser { Id = Guid.NewGuid(), UserName = "eleve" };
        db.Schools.Add(schule);
        db.Users.Add(schueler);
        db.StudentProfiles.Add(new StudentProfile { AppUserId = schueler.Id, SchoolId = schule.Id });
        await db.SaveChangesAsync();
        var dienst = new AntwortTonService(db);

        Assert.False(await dienst.IstAusAsync(schueler.Id));     // Standard: Stimme an
        Assert.True(await dienst.SetzeAsync(schueler.Id, true));
        Assert.True(await dienst.IstAusAsync(schueler.Id));
        Assert.True(await dienst.SetzeAsync(schueler.Id, false));
        Assert.False(await dienst.IstAusAsync(schueler.Id));
        Assert.False(await dienst.SetzeAsync(Guid.NewGuid(), true));  // ohne Schülerprofil
    }
}
