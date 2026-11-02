using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class ZyklusTeilnahmeServiceTests
{
    [Fact]
    public async Task AlteAboAktionenDuerfenNeueTeilnahmeNichtVeraendern()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", SubscriptionPriceOverride = 3000 };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
        var user = new AppUser { Id = Guid.NewGuid(), FullName = "Élève" };
        var profil = new StudentProfile { SchoolId = schule.Id, CurrentKlasseId = klasse.Id, AppUserId = user.Id };
        db.Schools.Add(schule); db.Klassen.Add(klasse); db.Users.Add(user); db.StudentProfiles.Add(profil);
        await db.SaveChangesAsync();
        var finanz = new FinanzService(db);
        Assert.True((await finanz.DurchlaufStartenAsync(schule.Id, klasse.Id, 10000, DateTime.Today, Guid.NewGuid())).Succeeded);
        var teilnahme = await db.LevelEnrollments.SingleAsync();
        var abo = new AboService(db);
        Assert.False((await abo.AboZuweisenAsync(user.Id, klasse.Id, AboTyp.SkoulouApp)).Succeeded);
        Assert.False((await abo.BezahltSetzenAsync(user.Id, klasse.Id)).Succeeded);
        Assert.False((await abo.AboEntfernenAsync(user.Id, klasse.Id, true)).Succeeded);
        Assert.False((await abo.AboEntfernenUndAusKlasseEntfernenAsync(user.Id, klasse.Id, true)).Succeeded);
        Assert.False((await finanz.SkoulouAppAboBuchungenErstellenAsync(teilnahme.Id, user.Id)).Succeeded);
        Assert.True(teilnahme.IsUnlocked); Assert.False(teilnahme.IsPaid);
        Assert.Null(teilnahme.PaidAtUtc); Assert.Null(teilnahme.StorniertAmUtc);
        Assert.Equal(klasse.Id, profil.CurrentKlasseId);
        Assert.Empty(await db.FinanzBuchungen.ToListAsync());

        // Auch inkonsistente Alt-/Importflags dürfen den Schutz nicht umgehen.
        teilnahme.IsPaid = true; teilnahme.PaidAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.False((await abo.BezahltZuruecknehmenAsync(user.Id, klasse.Id, true)).Succeeded);
        Assert.True(teilnahme.IsPaid);
    }

    [Fact]
    public async Task AngenommeneSelbstregistrierungZaehltTrotzPersoenlicherSperreAberFremdschuleNicht()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École" };
        var fremdschule = new School { Id = Guid.NewGuid(), Name = "Autre école" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
        var angenommen = new AppUser { Id = Guid.NewGuid(), IsSelfRegistered = true, IsBlocked = true };
        var offen = new AppUser { Id = Guid.NewGuid(), IsSelfRegistered = true, IsBlocked = true };
        var fremd = new AppUser { Id = Guid.NewGuid() };
        db.Schools.AddRange(schule, fremdschule); db.Klassen.Add(klasse); db.Users.AddRange(angenommen, offen, fremd);
        db.StudentProfiles.AddRange(
            new StudentProfile { SchoolId = schule.Id, CurrentKlasseId = klasse.Id, AppUserId = angenommen.Id, AufnahmeBestaetigtAmUtc = DateTime.UtcNow.AddDays(-1) },
            new StudentProfile { SchoolId = schule.Id, CurrentKlasseId = klasse.Id, AppUserId = offen.Id },
            new StudentProfile { SchoolId = fremdschule.Id, CurrentKlasseId = klasse.Id, AppUserId = fremd.Id });
        var zyklus = new KursNiveauDurchlauf { SchulID = schule.Id, KlasseId = klasse.Id, StartDatum = DateTime.Today, PlattformPreisProSchueler = 3000 };
        db.KursNiveauDurchlaeufe.Add(zyklus); await db.SaveChangesAsync();
        var service = new ZyklusTeilnahmeService(db);
        Assert.Equal(1, await service.TeilnehmerErgaenzenAsync(zyklus));
        await db.SaveChangesAsync();
        Assert.Equal(angenommen.Id, (await db.LevelEnrollments.SingleAsync()).StudentId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListeAsync(fremd.Id, zyklus.Id));
    }

    [Fact]
    public async Task AbschlussBerechnetAusgeschiedeneGesperrteUndSpaeteTeilnehmerEinmalig()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", SubscriptionPriceOverride = 3000 };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
        db.Schools.Add(schule); db.Klassen.Add(klasse);
        var profiles = new List<StudentProfile>();
        foreach (var name in new[] { "Départ", "Bloqué", "Attente", "Arrivée" })
        {
            var user = new AppUser { Id = Guid.NewGuid(), FullName = name, IsSelfRegistered = name == "Attente", IsBlocked = name == "Attente" };
            db.Users.Add(user);
            var p = new StudentProfile { SchoolId = schule.Id, AppUserId = user.Id, CurrentKlasseId = name == "Arrivée" ? null : klasse.Id };
            db.StudentProfiles.Add(p); profiles.Add(p);
        }
        await db.SaveChangesAsync();
        var finanz = new FinanzService(db);
        var (ok, fehler, zyklus) = await finanz.DurchlaufStartenAsync(schule.Id, klasse.Id, 10000, DateTime.Today.AddDays(-20), Guid.NewGuid());
        Assert.True(ok, fehler); Assert.NotNull(zyklus);
        Assert.Equal(2, await db.LevelEnrollments.CountAsync(x => x.DurchlaufId == zyklus.Id));
        profiles[0].CurrentKlasseId = null; profiles[0].IsArchived = true;
        (await db.Users.SingleAsync(x => x.Id == profiles[1].AppUserId)).IsBlocked = true;
        profiles[3].CurrentKlasseId = klasse.Id; await db.SaveChangesAsync();
        await new ZyklusTeilnahmeService(db).AktiveKlasseErfassenAsync(schule.Id, klasse.Id);
        profiles[3].CurrentKlasseId = null; await db.SaveChangesAsync();
        var geschlossen = await finanz.DurchlaufBeendenAsync(zyklus.Id, DateTime.Today);
        Assert.True(geschlossen.Succeeded, geschlossen.ErrorMessage);
        var rechnung = Assert.Single(await db.Abrechnungsperioden.ToListAsync());
        Assert.Equal(3, rechnung.AnzahlAbonnements); Assert.Equal(9000, rechnung.Umsatz);
        Assert.Equal(3, await db.LevelEnrollments.CountAsync(x => x.AbgerechnetInPeriodeId == rechnung.Id));
        Assert.False((await finanz.DurchlaufBeendenAsync(zyklus.Id, DateTime.Today)).Succeeded);
        Assert.Single(await db.Abrechnungsperioden.ToListAsync());
        Assert.All(await db.LevelEnrollments.ToListAsync(), x => { Assert.False(x.IsPaid); Assert.Null(x.PaidAtUtc); });
    }

    [Fact]
    public async Task FehlzuordnungNurAdminMitGrundUndOhneLoeschungStornierbar()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", SubscriptionPriceOverride = 2500 };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
        var admin = new AppUser { Id = Guid.NewGuid() }; var student = new AppUser { Id = Guid.NewGuid() };
        var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
        db.Schools.Add(schule); db.Klassen.Add(klasse); db.Users.AddRange(admin, student); db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = rolle.Id });
        db.StudentProfiles.Add(new StudentProfile { SchoolId = schule.Id, AppUserId = student.Id, CurrentKlasseId = klasse.Id });
        await db.SaveChangesAsync();
        var finanz = new FinanzService(db);
        var (_, _, zyklus) = await finanz.DurchlaufStartenAsync(schule.Id, klasse.Id, 10000, DateTime.Today.AddDays(-10), admin.Id);
        var teilnahme = await db.LevelEnrollments.SingleAsync(); var service = new ZyklusTeilnahmeService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StornierenAsync(student.Id, teilnahme.Id, "Erreur"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StornierenAsync(admin.Id, teilnahme.Id, " "));
        await service.StornierenAsync(admin.Id, teilnahme.Id, "Classe affectée par erreur");
        Assert.Equal(admin.Id, teilnahme.StorniertVonID); Assert.NotNull(teilnahme.StorniertAmUtc);
        Assert.Equal(0, await service.TeilnehmerErgaenzenAsync(zyklus!));
        Assert.Single(await service.ListeAsync(admin.Id, zyklus!.Id));
        Assert.True((await finanz.DurchlaufBeendenAsync(zyklus.Id, DateTime.Today)).Succeeded);
        Assert.Empty(await db.Abrechnungsperioden.ToListAsync()); Assert.Single(await db.LevelEnrollments.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StornierenAsync(admin.Id, teilnahme.Id, "Encore"));
    }

    [Fact]
    public async Task TeilnehmerSindEinmaligPreisfestUndKeineZahlung()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", SubscriptionPriceOverride = 3000 };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e", IstSystemNiveau = false };
        var user = new AppUser { Id = Guid.NewGuid(), FullName = "Élève" };
        db.Schools.Add(schule); db.Klassen.Add(klasse); db.Users.Add(user);
        db.StudentProfiles.Add(new StudentProfile { SchoolId = schule.Id, CurrentKlasseId = klasse.Id, AppUserId = user.Id });
        db.CommissionSettings.Add(new CommissionSettings { GlobalSubscriptionPrice = 5000 });
        await db.SaveChangesAsync();
        var service = new ZyklusTeilnahmeService(db);
        Assert.Equal(3000, await service.PreisErmittelnAsync(schule.Id));
        var zyklus = new KursNiveauDurchlauf { SchulID = schule.Id, KlasseId = klasse.Id, StartDatum = DateTime.Today, PlattformPreisProSchueler = 3000 };
        db.KursNiveauDurchlaeufe.Add(zyklus); await db.SaveChangesAsync();
        Assert.Equal(1, await service.TeilnehmerErgaenzenAsync(zyklus));
        Assert.Equal(0, await service.TeilnehmerErgaenzenAsync(zyklus));
        await db.SaveChangesAsync();
        schule.SubscriptionPriceOverride = 8000;
        var spaeter = new AppUser { Id = Guid.NewGuid(), FullName = "Arrivée tardive" };
        db.Users.Add(spaeter); db.StudentProfiles.Add(new StudentProfile { SchoolId = schule.Id, CurrentKlasseId = klasse.Id, AppUserId = spaeter.Id });
        await db.SaveChangesAsync();
        Assert.Equal(1, await service.TeilnehmerErgaenzenAsync(zyklus)); await db.SaveChangesAsync();
        Assert.Equal(2, await db.LevelEnrollments.CountAsync());
        Assert.All(await db.LevelEnrollments.ToListAsync(), x =>
        {
            Assert.Equal(3000, x.PriceAtEnrollment); Assert.True(x.IsUnlocked); Assert.False(x.IsPaid); Assert.Null(x.PaidAtUtc);
            Assert.Equal(0, x.SchoolLeaderCommissionAtEnrollment); Assert.Equal(0, x.TeacherCommissionAtEnrollment); Assert.Equal(0, x.ProductOwnerCommissionAtEnrollment);
        });
        Assert.Empty(await db.FinanzBuchungen.ToListAsync());
        schule.SubscriptionPriceOverride = null; await db.SaveChangesAsync();
        Assert.Equal(5000, await service.PreisErmittelnAsync(schule.Id));
        zyklus.EndDatum = DateTime.Today;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TeilnehmerErgaenzenAsync(zyklus));
    }
}
