using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für <see cref="AboService"/>: Abo-Zuweisung, Preis-Einfrieren
/// und die (un)widerruflichen Bezahlregeln.
/// </summary>
public class AboServiceTests
{
    private static (School Schule, AppUser Student, Klasse Level) SeedBasis(
        TestDbContext db,
        decimal? preisBasis = null,
        decimal? preisPlus = null)
    {
        var schule = new School
        {
            Id = Guid.NewGuid(),
            Name = "Schule A",
            IsActive = true,
            SubscriptionPriceOverride = preisPlus ?? preisBasis
        };

        var studentUser = new AppUser { Id = Guid.NewGuid(), FullName = "Schüler", UserName = "schueler" };

        var level = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Anfänger" };

        var profil = new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentUser.Id,
            SchoolId = schule.Id
        };

        db.Schools.Add(schule);
        db.Users.Add(studentUser);
        db.Klassen.Add(level);
        db.StudentProfiles.Add(profil);
        db.SaveChanges();

        return (schule, studentUser, level);
    }

    private static async Task StarteAktivenDurchlaufAsync(TestDbContext db, School schule, Klasse level)
    {
        db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = schule.Id,
            KlasseId = level.Id,
            NiveauPreis = level.Price > 0m ? level.Price : 12500m,
            StartDatum = DateTime.UtcNow.AddDays(-1),
            EndDatum = null,
            ErstelltVonUserID = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AboZuweisenAsync_SchaltetNiveauFreiUndFriertStandardpreisEin()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);

        // Act
        var (succeeded, _) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Assert
        Assert.True(succeeded);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.True(enrollment.IsUnlocked);
        Assert.True(enrollment.IsPaid);
        Assert.NotNull(enrollment.PaidAtUtc);
        Assert.Equal(AboTyp.SkoulouApp, enrollment.AboTyp);
        Assert.Equal(SkoulouAppKonstanten.StandardPreisAbo, enrollment.PriceAtEnrollment);
    }

    [Fact]
    public async Task AboZuweisenAsync_VerwendetSchulspezifischenPreis()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db, preisBasis: 5000m);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);

        // Act
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Assert
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.Equal(5000m, enrollment.PriceAtEnrollment);
    }

    [Fact]
    public async Task AboZuweisenAsync_VerwendetSchulspezifischeCommissionOverrides()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db, preisBasis: 10000m);
        schule.SchoolLeaderCommissionPercentOverride = 10m;
        schule.TeacherCommissionPercentOverride = 20m;
        schule.ProductOwnerCommissionPercentOverride = 5m;
        db.CommissionSettings.Add(new CommissionSettings
        {
            GlobalSubscriptionPrice = 10000m,
            SchoolLeaderPercent = 1m,
            TeacherPercent = 1m,
            ProductOwnerPercent = 1m
        });
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);

        // Act
        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Assert
        Assert.True(succeeded, message);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.Equal(1000m, enrollment.SchoolLeaderCommissionAtEnrollment);
        Assert.Equal(2000m, enrollment.TeacherCommissionAtEnrollment);
        Assert.Equal(500m, enrollment.ProductOwnerCommissionAtEnrollment);
    }

    [Fact]
    public async Task AboZuweisenAsync_InitialisiertBestehendeKlassenfreischaltungAlsSkoulouAppAbo()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db, preisBasis: 12500m);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = student.Id,
            KlasseId = level.Id,
            IsUnlocked = true,
            AboTyp = AboTyp.SkoulouApp,
            PriceAtEnrollment = 50000m,
            SchoolLeaderCommissionAtEnrollment = 0m,
            TeacherCommissionAtEnrollment = 0m
        });
        await db.SaveChangesAsync();
        var service = new AboService(db);

        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        Assert.True(succeeded, message);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.Equal(12500m, enrollment.PriceAtEnrollment);
        Assert.True(enrollment.SchoolLeaderCommissionAtEnrollment > 0m);
        Assert.True(enrollment.TeacherCommissionAtEnrollment > 0m);
    }

    [Fact]
    public async Task AboZuweisenAsync_OhneAktivenDurchlauf_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (_, student, level) = SeedBasis(db);
        var service = new AboService(db);

        // Act
        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("cycle", message);
        Assert.Empty(db.LevelEnrollments);
    }

    [Fact]
    public async Task AboZuweisenAsync_OhneProfil_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var service = new AboService(db);

        // Act
        var (succeeded, message) = await service.AboZuweisenAsync(Guid.NewGuid(), Guid.NewGuid(), AboTyp.SkoulouApp);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("profil", message);
    }

    [Fact]
    public async Task AboZuweisenAsync_BereitsFinalesSkoulouAppNachZahlung_KannNichtErneutGesetztWerden()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);

        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = student.Id,
            KlasseId = level.Id,
            AboTyp = AboTyp.SkoulouApp,
            IsUnlocked = true,
            IsPaid = false,
            PriceAtEnrollment = SkoulouAppKonstanten.StandardPreisAbo,
            SchoolLeaderCommissionAtEnrollment = SkoulouAppKonstanten.ProvisionSchulleiterBasis,
            TeacherCommissionAtEnrollment = SkoulouAppKonstanten.ProvisionLehrerBasis,
            ProductOwnerCommissionAtEnrollment = SkoulouAppKonstanten.ProvisionProductOwnerBasis,
            UnlockedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        await service.BezahltSetzenAsync(student.Id, level.Id);

        // Act
        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("déjà attribué", message);
        Assert.Equal(AboTyp.SkoulouApp, db.LevelEnrollments.Single().AboTyp);
    }

    [Fact]
    public async Task AboZuweisenAsync_SkoulouBasisKannNichtErneutFestgelegtWerden()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        Assert.False(succeeded);
        Assert.Contains("déjà attribué", message);
    }

    [Fact]
    public async Task AboZuweisenAsync_SkoulouPlusKannNichtZuBasisHerabgestuftWerden()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        Assert.False(succeeded);
        Assert.Contains("SKOULOUAPP", message);
        Assert.Equal(AboTyp.SkoulouApp, db.LevelEnrollments.Single().AboTyp);
    }

    [Fact]
    public async Task BezahltSetzenAsync_SetztBezahltstatusUndZeitstempel()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        var (undoSucceeded, undoMessage) = await service.BezahltZuruecknehmenAsync(student.Id, level.Id, istSuperAdmin: true);
        Assert.True(undoSucceeded, undoMessage);

        // Act
        var (succeeded, message) = await service.BezahltSetzenAsync(student.Id, level.Id);

        // Assert
        Assert.True(succeeded, message);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.True(enrollment.IsPaid);
        Assert.NotNull(enrollment.PaidAtUtc);
    }

    [Fact]
    public async Task BezahltZuruecknehmenAsync_NichtAbgerechnet_MachtZahlungRueckgaengig()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await service.BezahltSetzenAsync(student.Id, level.Id);

        // Act
        var (succeeded, _) = await service.BezahltZuruecknehmenAsync(student.Id, level.Id, istSuperAdmin: true);

        // Assert
        Assert.True(succeeded);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.False(enrollment.IsPaid);
        Assert.Null(enrollment.PaidAtUtc);
    }

    [Fact]
    public async Task BezahltZuruecknehmenAsync_OhneSuperAdmin_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await service.BezahltSetzenAsync(student.Id, level.Id);

        // Act
        var (succeeded, message) = await service.BezahltZuruecknehmenAsync(student.Id, level.Id);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("Administrateur", message);
        Assert.True(db.LevelEnrollments.Single().IsPaid);
    }

    [Fact]
    public async Task BezahltZuruecknehmenAsync_GeschlossenerDurchlauf_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await service.BezahltSetzenAsync(student.Id, level.Id);

        db.KursNiveauDurchlaeufe.Single().EndDatum = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Act
        var (succeeded, message) = await service.BezahltZuruecknehmenAsync(student.Id, level.Id, istSuperAdmin: true);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("cycle", message);
        Assert.True(db.LevelEnrollments.Single().IsPaid);
    }

    [Fact]
    public async Task BezahltZuruecknehmenAsync_BereitsAbgerechnet_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await service.BezahltSetzenAsync(student.Id, level.Id);

        var periode = new Abrechnungsperiode
        {
            Id = Guid.NewGuid(),
            SchulID = schule.Id,
            VonUtc = DateTime.UtcNow.AddDays(-30),
            BisUtc = DateTime.UtcNow,
            IstAbgeschlossen = true
        };
        db.Abrechnungsperioden.Add(periode);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.AbgerechnetInPeriodeId = periode.Id;
        await db.SaveChangesAsync();

        // Act
        var (succeeded, message) = await service.BezahltZuruecknehmenAsync(student.Id, level.Id, istSuperAdmin: true);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("facturé", message);
    }

    [Fact]
    public async Task AboZuweisenAsync_HoeheresNiveau_StuftAktuellesNiveauHoch()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var hoeheresLevel = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "Grundstufe" };
        db.Klassen.Add(hoeheresLevel);
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, hoeheresLevel);

        var service = new AboService(db);

        // Act
        await service.AboZuweisenAsync(student.Id, hoeheresLevel.Id, AboTyp.SkoulouApp);

        // Assert
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        Assert.Equal("A2.1", profil.CurrentLevelCode);
    }

    [Fact]
    public async Task AboZuweisenAsync_NiedrigeresNiveau_StuftAktuellesNiveauNichtZurueck()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var hoeheresLevel = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "Grundstufe" };
        db.Klassen.Add(hoeheresLevel);
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, hoeheresLevel);
        await StarteAktivenDurchlaufAsync(db, schule, level);

        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, hoeheresLevel.Id, AboTyp.SkoulouApp);

        // Act – niedrigeres Niveau freischalten
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Assert – aktuelles Niveau bleibt beim höheren
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        Assert.Equal("A2.1", profil.CurrentLevelCode);
    }

    [Fact]
    public async Task AboEntfernenAsync_NichtBezahlt_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        // Act
        var (succeeded, _) = await service.AboEntfernenAsync(student.Id, level.Id);

        // Assert
        Assert.False(succeeded);
        Assert.Single(db.LevelEnrollments);
    }

    [Fact]
    public async Task AboEntfernenAsync_Bezahlt_StorniertAboUndBuchungen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        Assert.NotEmpty(db.FinanzBuchungen);

        // Act
        var (succeeded, message) = await service.AboEntfernenAsync(student.Id, level.Id, istSuperAdmin: true);

        // Assert
        Assert.True(succeeded, message);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.False(enrollment.IsUnlocked);
        Assert.False(enrollment.IsPaid);
        Assert.Null(enrollment.PaidAtUtc);
        Assert.True(enrollment.IstAktuell);
        Assert.Empty(db.FinanzBuchungen.Where(x => x.HerkunftReferenzID == enrollment.Id));
    }

    [Fact]
    public async Task AboEntfernenAsync_ReineStornierung_BelaesstSchuelerInKlasse()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        profil.CurrentKlasseId = level.Id;
        profil.CurrentLevelCode = level.Code;
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        var (succeeded, message) = await service.AboEntfernenAsync(student.Id, level.Id, istSuperAdmin: true);

        Assert.True(succeeded, message);
        profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        Assert.Equal(level.Id, profil.CurrentKlasseId);
        Assert.Equal(level.Code, profil.CurrentLevelCode);
        Assert.True(db.LevelEnrollments.Single().IstAktuell);
    }

    [Fact]
    public async Task AboEntfernenUndAusKlasseEntfernenAsync_StorniertUndTrenntKlasse()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        profil.CurrentKlasseId = level.Id;
        profil.CurrentLevelCode = level.Code;
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        var (succeeded, message) = await service.AboEntfernenUndAusKlasseEntfernenAsync(
            student.Id,
            level.Id,
            istSuperAdmin: true);

        Assert.True(succeeded, message);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.False(enrollment.IsUnlocked);
        Assert.False(enrollment.IsPaid);
        Assert.False(enrollment.IstAktuell);
        Assert.Null(enrollment.PaidAtUtc);
        Assert.Empty(db.FinanzBuchungen.Where(x => x.HerkunftReferenzID == enrollment.Id));
        profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        Assert.Null(profil.CurrentKlasseId);
        Assert.Equal(string.Empty, profil.CurrentLevelCode);
    }

    [Fact]
    public async Task AboEntfernenUndAusKlasseEntfernenAsync_OhneSuperAdmin_BleibtUnveraendert()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        profil.CurrentKlasseId = level.Id;
        profil.CurrentLevelCode = level.Code;
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        var (succeeded, _) = await service.AboEntfernenUndAusKlasseEntfernenAsync(student.Id, level.Id);

        Assert.False(succeeded);
        Assert.True(db.LevelEnrollments.Single().IstAktuell);
        Assert.Equal(level.Id, db.StudentProfiles.Single().CurrentKlasseId);
    }

    [Fact]
    public async Task AboEntfernenUndAusKlasseEntfernenAsync_BereitsAbgerechnet_BleibtUnveraendert()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        profil.CurrentKlasseId = level.Id;
        profil.CurrentLevelCode = level.Code;
        await db.SaveChangesAsync();
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.AbgerechnetInPeriodeId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var (succeeded, message) = await service.AboEntfernenUndAusKlasseEntfernenAsync(
            student.Id,
            level.Id,
            istSuperAdmin: true);

        Assert.False(succeeded);
        Assert.Contains("déjà été facturé", message);
        Assert.True(enrollment.IstAktuell);
        Assert.Equal(level.Id, db.StudentProfiles.Single().CurrentKlasseId);
    }

    [Fact]
    public async Task AboEntfernenAsync_BereitsAbgerechnet_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);
        await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await service.BezahltSetzenAsync(student.Id, level.Id);

        var periode = new Abrechnungsperiode
        {
            Id = Guid.NewGuid(),
            SchulID = schule.Id,
            VonUtc = DateTime.UtcNow.AddDays(-30),
            BisUtc = DateTime.UtcNow,
            IstAbgeschlossen = true
        };
        db.Abrechnungsperioden.Add(periode);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.AbgerechnetInPeriodeId = periode.Id;
        await db.SaveChangesAsync();

        // Act
        var (succeeded, message) = await service.AboEntfernenAsync(student.Id, level.Id, istSuperAdmin: true);

        // Assert
        Assert.False(succeeded);
        Assert.Single(db.LevelEnrollments);
        Assert.Contains("déjà été facturé", message);
    }

    [Fact]
    public async Task AboZuweisenAsync_NachDeaktivierungUndErneuterKlassenzuordnung_KannWiederAktiviertWerden()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        profil.CurrentKlasseId = level.Id;
        profil.CurrentLevelCode = level.Code;
        await db.SaveChangesAsync();

        await StarteAktivenDurchlaufAsync(db, schule, level);
        var service = new AboService(db);

        var (firstAssignSucceeded, firstAssignMessage) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        Assert.True(firstAssignSucceeded, firstAssignMessage);

        var (undoPaymentSucceeded, undoPaymentMessage) = await service.BezahltZuruecknehmenAsync(student.Id, level.Id, istSuperAdmin: true);
        Assert.True(undoPaymentSucceeded, undoPaymentMessage);

        var (removeSucceeded, removeMessage) = await service.AboEntfernenAsync(student.Id, level.Id, istSuperAdmin: true);
        Assert.True(removeSucceeded, removeMessage);

        profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        Assert.Equal(level.Id, profil.CurrentKlasseId);
        Assert.Equal(level.Code, profil.CurrentLevelCode);

        var (reassignSucceeded, reassignMessage) = await service.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);

        Assert.True(reassignSucceeded, reassignMessage);
        var enrollment = Assert.Single(db.LevelEnrollments);
        Assert.True(enrollment.IsUnlocked);
        Assert.True(enrollment.IsPaid);
    }
}
