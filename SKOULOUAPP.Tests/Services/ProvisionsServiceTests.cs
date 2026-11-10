using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Billing;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Historische Provisionssnapshots bleiben für Bestandsabrechnungen nachvollziehbar.
/// Neue Zyklusrechnungen dürfen daraus keine Schul- oder Lehreranteile erfinden.
/// </summary>
public class ProvisionsServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3000.001)]
    public async Task UngueltigePreiseWerdenNichtGespeichert(decimal preis)
    {
        using var db = TestDbContext.CreateInMemory();
        var adminID = await TestIdentityFactory.AddSuperAdminAsync(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProvisionsService(db)
            .SaveCommissionSettingsAsync(new() { GlobalSubscriptionPrice = preis }, adminID));
        var service = new DashboardService(db, TestUserManager.Create(), new FakeMediaStorageService());
        Assert.False((await service.SaveSchoolAsync(new() { Name = "Prix", SubscriptionPriceOverride = preis }, adminID)).Succeeded);
        Assert.Empty(db.Schools); Assert.Empty(db.CommissionSettings);
    }

    [Fact]
    public async Task PreisverwaltungLehntUnbekannteUndGesperrteAdministratorenAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new ProvisionsService(db);
        var model = new CommissionSettingsModel { GlobalSubscriptionPrice = 3000m };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveCommissionSettingsAsync(model, Guid.NewGuid()));
        var adminID = await TestIdentityFactory.AddSuperAdminAsync(db);
        db.Users.Single(x => x.Id == adminID).IsBlocked = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveCommissionSettingsAsync(model, adminID));
        Assert.Empty(db.CommissionSettings);
        var dashboard = new DashboardService(db, TestUserManager.Create(), new FakeMediaStorageService());
        Assert.False((await dashboard.SaveSchoolAsync(new() { Name = "Interdite" }, adminID)).Succeeded);
        Assert.Empty(db.Schools);
    }

    [Fact]
    public async Task NeueZyklusrechnungErfindetKeineHistorischenStandardprovisionen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (school, student, _, level) = SeedBasis(db);
        var cycle = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = school.Id, KlasseId = level.Id, PlattformPreisProSchueler = 3000m };
        var period = new Abrechnungsperiode { Id = Guid.NewGuid(), SchulID = school.Id, Umsatz = 3000m, AnzahlAbonnements = 1 };
        db.KursNiveauDurchlaeufe.Add(cycle); db.Abrechnungsperioden.Add(period);
        db.LevelEnrollments.Add(new LevelEnrollment { StudentId = student.Id, KlasseId = level.Id,
            DurchlaufId = cycle.Id, AbgerechnetInPeriodeId = period.Id, PriceAtEnrollment = 3000m, IsPaid = false });
        await db.SaveChangesAsync();
        var result = await new ProvisionsService(db).GetPeriodeUebersichtAsync(period.Id);
        Assert.Equal(0m, result.ProvisionLehrer);
        Assert.Equal(3000m, result.Anteile.Where(x => x.Empfaenger == ProvisionsEmpfaenger.Admin).Sum(x => x.Betrag));
        Assert.DoesNotContain(result.Anteile, x => x.Empfaenger is ProvisionsEmpfaenger.Schulleiter or ProvisionsEmpfaenger.Lehrer or ProvisionsEmpfaenger.ProductOwner);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_BerechnetLehrerpoolOhneIndividuelleAuszahlungen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, firstTeacher, level) = SeedBasis(db);
        var secondTeacher = new AppUser { Id = Guid.NewGuid(), FullName = "Lehrer Zwei", UserName = "lehrer-zwei" };
        db.Users.Add(secondTeacher);
        var firstAssignment = db.LehrerNiveauZuordnungen.Single();
        firstAssignment.Wochenstunden = 6m;
        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung
        {
            Id = Guid.NewGuid(),
            LehrerID = secondTeacher.Id,
            SchulID = schule.Id,
            KlasseId = level.Id,
            Wochenstunden = 4m
        });
        await db.SaveChangesAsync();

        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var billing = new AbrechnungService(db);
        await billing.AbrechnungErstellenAsync(schule.Id);
        var period = db.Abrechnungsperioden.Single();
        await billing.SchulLeiterStatusSetzenAsync(period.Id, akzeptiert: true);
        await billing.AdminBestaetigenAsync(period.Id, await TestIdentityFactory.AddSuperAdminAsync(db));

        Assert.Empty(db.LehrerProvisionsAuszahlungen);

        var service = new ProvisionsService(db);
        var receipt = await service.LehrerErhaltBestaetigenAsync(firstTeacher.Id, period.Id);

        Assert.False(receipt.Succeeded);
        Assert.Contains("n'est plus nécessaire", receipt.Message);
        var uebersicht = await service.GetPeriodeUebersichtAsync(period.Id);
        Assert.Equal(SkoulouAppKonstanten.ProvisionLehrerBasis, uebersicht.ProvisionLehrer);
    }

    private static (School Schule, AppUser Student, AppUser Lehrer, Klasse Level) SeedBasis(
        TestDbContext db, bool mitLehrerZuordnung = true)
    {
        var schule = new School
        {
            Id = Guid.NewGuid(),
            Name = "Schule A",
            IsActive = true,
            ErstelltAmUtc = DateTime.UtcNow.AddYears(-1)
        };

        var studentUser = new AppUser { Id = Guid.NewGuid(), FullName = "Schüler", UserName = "schueler" };
        var lehrerUser = new AppUser { Id = Guid.NewGuid(), FullName = "Lehrer", UserName = "lehrer" };
        var level = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM1A", Title = "CM1 A" };
        var profil = new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentUser.Id,
            SchoolId = schule.Id
        };

        db.Schools.Add(schule);
        db.Users.Add(studentUser);
        db.Users.Add(lehrerUser);
        db.UserSchools.Add(new UserSchool { AppUserId = lehrerUser.Id, SchoolId = schule.Id });
        db.Klassen.Add(level);
        db.StudentProfiles.Add(profil);

        if (mitLehrerZuordnung)
        {
            db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung
            {
                Id = Guid.NewGuid(),
                LehrerID = lehrerUser.Id,
                SchulID = schule.Id,
                KlasseId = level.Id
            });
        }

        db.SaveChanges();
        db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = schule.Id,
            KlasseId = level.Id,
            NiveauPreis = 12500m,
            StartDatum = DateTime.UtcNow.AddDays(-1),
            EndDatum = null,
            ErstelltVonUserID = Guid.NewGuid()
        });
        db.SaveChanges();
        return (schule, studentUser, lehrerUser, level);
    }

    [Fact]
    public async Task GetOffeneUebersichtAsync_VerteiltBasisProvisionKorrekt()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new ProvisionsService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);

        // Assert
        Assert.Equal(1, uebersicht.AnzahlPositionen);
        Assert.Equal(SkoulouAppKonstanten.ProvisionSchulleiterBasis, uebersicht.ProvisionSchulleiter);
        Assert.Equal(SkoulouAppKonstanten.ProvisionLehrerBasis, uebersicht.ProvisionLehrer);
        Assert.Equal(
            SkoulouAppKonstanten.StandardPreisAbo
                - SkoulouAppKonstanten.ProvisionSchulleiterBasis
                - SkoulouAppKonstanten.ProvisionLehrerBasis,
            uebersicht.ProvisionAdmin);
        Assert.Equal(SkoulouAppKonstanten.StandardPreisAbo, uebersicht.Gesamtumsatz);
    }

    [Fact]
    public async Task GetOffeneUebersichtAsync_VerteiltPlusProvisionKorrekt()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new ProvisionsService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);

        // Assert
        Assert.Equal(SkoulouAppKonstanten.ProvisionSchulleiterPlus, uebersicht.ProvisionSchulleiter);
        Assert.Equal(SkoulouAppKonstanten.ProvisionLehrerPlus, uebersicht.ProvisionLehrer);
        Assert.Equal(
            SkoulouAppKonstanten.StandardPreisAbo
                - SkoulouAppKonstanten.ProvisionSchulleiterPlus
                - SkoulouAppKonstanten.ProvisionLehrerPlus,
            uebersicht.ProvisionAdmin);
        Assert.Equal(SkoulouAppKonstanten.StandardPreisAbo, uebersicht.Gesamtumsatz);
    }

    [Fact]
    public async Task AboZuweisenAsync_BerechnetKommissionenProzentualAusDemSchulpreisUndFriertSieEin()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db);
        schule.SubscriptionPriceOverride = 20000m;
        db.CommissionSettings.Add(new CommissionSettings
        {
            Id = Guid.NewGuid(),
            SchoolLeaderPercent = 4m,
            TeacherPercent = 8m,
            ProductOwnerPercent = 2m
        });
        await db.SaveChangesAsync();

        var abo = new AboService(db);
        var result = await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        Assert.True(result.Succeeded);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.Equal(20000m, enrollment.PriceAtEnrollment);
        Assert.Equal(800m, enrollment.SchoolLeaderCommissionAtEnrollment);
        Assert.Equal(1600m, enrollment.TeacherCommissionAtEnrollment);
        Assert.Equal(400m, enrollment.ProductOwnerCommissionAtEnrollment);
    }

    [Fact]
    public async Task SaveCommissionSettingsAsync_LehntAnteileUeberHundertProzentAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new ProvisionsService(db);
        var adminID = await TestIdentityFactory.AddSuperAdminAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCommissionSettingsAsync(new CommissionSettingsModel
        {
            SchoolLeaderPercent = 60m,
            TeacherPercent = 50m
        }, adminID));
    }

    [Fact]
    public async Task GetOffeneUebersichtAsync_IgnoriertUnbezahltePositionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.IsPaid = false;
        enrollment.PaidAtUtc = null;
        await db.SaveChangesAsync();
        var service = new ProvisionsService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);

        // Assert
        Assert.Equal(0, uebersicht.AnzahlPositionen);
        Assert.Equal(0m, uebersicht.Gesamtumsatz);
    }

    [Fact]
    public async Task BerechneAnteile_OhneLehrerZuordnung_LaesstLehrerIdLeer()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db, mitLehrerZuordnung: false);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new ProvisionsService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);

        // Assert
        var lehrerAnteil = Assert.Single(uebersicht.Anteile, x => x.Empfaenger == ProvisionsEmpfaenger.Lehrer);
        Assert.Null(lehrerAnteil.LehrerID);
        Assert.Equal(SkoulouAppKonstanten.ProvisionLehrerBasis, lehrerAnteil.Betrag);
    }

    [Fact]
    public async Task GetPeriodeUebersichtAsync_LiefertAbgerechneteProvisionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var abrechnung = new AbrechnungService(db);
        await abrechnung.AbrechnungErstellenAsync(schule.Id);
        var periode = db.Abrechnungsperioden.Single();
        var service = new ProvisionsService(db);

        // Act
        var uebersicht = await service.GetPeriodeUebersichtAsync(periode.Id);

        // Assert
        Assert.Equal(periode.Id, uebersicht.PeriodeID);
        Assert.Equal(1, uebersicht.AnzahlPositionen);
        Assert.Equal(SkoulouAppKonstanten.StandardPreisAbo, uebersicht.Gesamtumsatz);
    }

    [Fact]
    public async Task GetLehrerUebersichtenAsync_LiefertNurEigeneAbgeschlosseneProvisionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, lehrer, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var abrechnung = new AbrechnungService(db);
        await abrechnung.AbrechnungErstellenAsync(schule.Id);
        var periode = db.Abrechnungsperioden.Single();
        await abrechnung.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: true);
        await abrechnung.AdminBestaetigenAsync(periode.Id, await TestIdentityFactory.AddSuperAdminAsync(db));
        var service = new ProvisionsService(db);

        // Act
        var meineUebersicht = await service.GetLehrerUebersichtenAsync(lehrer.Id);
        var fremdeUebersicht = await service.GetLehrerUebersichtenAsync(Guid.NewGuid());

        // Assert
        var eintrag = Assert.Single(meineUebersicht);
        Assert.Equal(SkoulouAppKonstanten.ProvisionLehrerBasis, eintrag.ProvisionLehrer);
        Assert.Equal(SkoulouAppKonstanten.ProvisionSchulleiterBasis, eintrag.ProvisionSchulleiter);
        Assert.Equal(
            SkoulouAppKonstanten.StandardPreisAbo
                - SkoulouAppKonstanten.ProvisionSchulleiterBasis
                - SkoulouAppKonstanten.ProvisionLehrerBasis,
            eintrag.ProvisionAdmin);
        Assert.Equal(SkoulouAppKonstanten.ProvisionLehrerBasis, eintrag.SichtbarerBetrag);
        Assert.Empty(fremdeUebersicht);
    }

    [Fact]
    public async Task GetLehrerUebersichtenAsync_IgnoriertNichtAbgeschlossenePerioden()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, lehrer, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var abrechnung = new AbrechnungService(db);
        await abrechnung.AbrechnungErstellenAsync(schule.Id);
        var service = new ProvisionsService(db);

        // Act (Periode ist erstellt, aber noch nicht abgeschlossen)
        var uebersichten = await service.GetLehrerUebersichtenAsync(lehrer.Id);

        // Assert
        Assert.Empty(uebersichten);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_ProduktOwnerSiehtAggregiertenAnteilSeinerZugewiesenenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, _, level) = SeedBasis(db);
        var produktOwner = new AppUser { Id = Guid.NewGuid(), FullName = "Product Owner", UserName = "owner" };
        var produktOwnerRole = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner, NormalizedName = "PRODUKTOWNER" };
        db.Users.Add(produktOwner);
        db.Roles.Add(produktOwnerRole);
        db.UserRoles.Add(new AppUserRole { UserId = produktOwner.Id, RoleId = produktOwnerRole.Id });
        db.UserSchools.Add(new UserSchool { AppUserId = produktOwner.Id, SchoolId = schule.Id, CanManageStudents = true });
        db.CommissionSettings.Add(new CommissionSettings
        {
            Id = Guid.NewGuid(),
            SchoolLeaderPercent = 4m,
            TeacherPercent = 8m,
            ProductOwnerPercent = 2m
        });
        await db.SaveChangesAsync();

        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var enrollment = db.LevelEnrollments.Single();

        var abrechnung = new AbrechnungService(db);
        var result = await abrechnung.AbrechnungErstellenAsync(schule.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(db.ProduktOwnerProvisionsAuszahlungen);

        var periode = db.Abrechnungsperioden.Single();
        await abrechnung.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: true);
        await abrechnung.AdminBestaetigenAsync(periode.Id, await TestIdentityFactory.AddSuperAdminAsync(db));
        var uebersicht = Assert.Single(await new ProvisionsService(db).GetProduktOwnerUebersichtenAsync(produktOwner.Id));
        Assert.Equal(enrollment.ProductOwnerCommissionAtEnrollment, uebersicht.ProvisionProductOwner);
        Assert.Equal(enrollment.ProductOwnerCommissionAtEnrollment, uebersicht.SichtbarerBetrag);
    }
}
