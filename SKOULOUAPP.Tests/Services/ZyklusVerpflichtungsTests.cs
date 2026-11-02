using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class ZyklusVerpflichtungsTests
{
    [Fact]
    public async Task UebersichtTrenntUnbezahlteTeilnahmeStornoUndGemeldeteRechnungJeSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var admin = new AppUser { Id = Guid.NewGuid() };
        var role = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
        var school = new School { Id = Guid.NewGuid(), Name = "A" };
        var other = new School { Id = Guid.NewGuid(), Name = "B" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = school.Id, Code = "CM1" };
        var cycle = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = klasse.Id, PlattformPreisProSchueler = 3000m, StartDatum = DateTime.UtcNow };
        db.Users.Add(admin); db.Roles.Add(role); db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = role.Id });
        db.Schools.AddRange(school, other); db.Klassen.Add(klasse); db.KursNiveauDurchlaeufe.Add(cycle);
        db.LevelEnrollments.AddRange(
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), KlasseId = klasse.Id, DurchlaufId = cycle.Id, PriceAtEnrollment = 3000m, IsPaid = false, IstAktuell = false },
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), KlasseId = klasse.Id, DurchlaufId = cycle.Id, PriceAtEnrollment = 3000m, IsPaid = false },
            new LevelEnrollment { Id = Guid.NewGuid(), StudentId = Guid.NewGuid(), KlasseId = klasse.Id, DurchlaufId = cycle.Id, PriceAtEnrollment = 3000m, StorniertAmUtc = DateTime.UtcNow });
        db.Abrechnungsperioden.AddRange(
            new Abrechnungsperiode { SchulID = school.Id, Umsatz = 6000m, Status = AbrechnungStatus.Erstellt },
            new Abrechnungsperiode { SchulID = school.Id, Umsatz = 3000m, Status = AbrechnungStatus.Akzeptiert },
            new Abrechnungsperiode { SchulID = school.Id, Umsatz = 2000m, Status = AbrechnungStatus.Bestaetigt },
            new Abrechnungsperiode { SchulID = other.Id, Umsatz = 99000m, Status = AbrechnungStatus.Erstellt });
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);
        var overview = await service.GetZyklusVerpflichtungenAsync(school.Id, admin.Id);
        Assert.Equal(2, overview.TeilnehmerAnzahl); Assert.Equal(6000m, overview.VorlaeufigerBetrag);
        Assert.Equal(9000m, overview.OffeneRechnungenBetrag); Assert.Equal(3000m, overview.ZahlungGemeldetBetrag);
        Assert.Equal(2, overview.OffeneRechnungenAnzahl); Assert.Single(overview.LaufendeZyklen);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetZyklusVerpflichtungenAsync(school.Id, Guid.NewGuid()));
        admin.IsBlocked = true; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetZyklusVerpflichtungenAsync(school.Id, admin.Id));
    }
}
