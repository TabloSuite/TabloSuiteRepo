using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Nachabrechnung und Zahlungsworkflow historischer Zyklen ohne Plattformpreis-Snapshot.
/// Automatische Rechnungen neuer Zyklen werden in ZyklusTeilnahmeServiceTests geprüft.
/// </summary>
public class AbrechnungServiceTests
{
    private static (School Schule, AppUser Student, Klasse Level) SeedBasis(TestDbContext db)
    {
        var schule = new School
        {
            Id = Guid.NewGuid(),
            Name = "Schule A",
            IsActive = true,
            ErstelltAmUtc = DateTime.UtcNow.AddYears(-1)
        };

        var studentUser = new AppUser { Id = Guid.NewGuid(), FullName = "Schüler", UserName = "schueler" };
        var level = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM1A", Title = "CM1 A" };
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

        return (schule, studentUser, level);
    }

    [Fact]
    public async Task GetOffeneUebersichtAsync_ZaehltNurBezahlteOffenePositionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);

        // Assert
        Assert.Equal(1, uebersicht.AnzahlAbonnements);
        Assert.Equal(
            SkoulouAppKonstanten.StandardPreisAbo
                - SkoulouAppKonstanten.ProvisionSchulleiterBasis
                - SkoulouAppKonstanten.ProvisionLehrerBasis,
            uebersicht.OffenerUmsatz);
    }

    [Fact]
    public async Task GetOffeneUebersichtAsync_IgnoriertUnbezahltePositionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.IsPaid = false;
        enrollment.PaidAtUtc = null;
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);

        // Assert
        Assert.Equal(0, uebersicht.AnzahlPositionen);
        Assert.Equal(0m, uebersicht.OffenerUmsatz);
    }

    [Fact]
    public async Task GetOffeneForderungenAusGeschlossenenKlassenAsync_LiefertNurOffeneAbosAusGeschlossenenZyklen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var closedCycle = db.KursNiveauDurchlaeufe.Single();
        closedCycle.StartDatum = DateTime.UtcNow.AddDays(-10);
        closedCycle.EndDatum = DateTime.UtcNow.AddDays(-2);

        db.LevelEnrollments.AddRange(
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = DateTime.UtcNow.AddDays(-5),
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m
            },
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = DateTime.UtcNow.AddDays(-5),
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m,
                AbgerechnetInPeriodeId = Guid.NewGuid()
            },
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = DateTime.UtcNow,
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m
            });
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        var forderungen = await service.GetOffeneForderungenAusGeschlossenenKlassenAsync();

        var forderung = Assert.Single(forderungen);
        Assert.Equal(schule.Id, forderung.SchulID);
        Assert.Equal(level.Id, forderung.KlasseId);
        Assert.Equal(1, forderung.AnzahlAbonnements);
        Assert.Equal(11_000m, forderung.Plattformforderung);
    }

    [Fact]
    public async Task SammelabrechnungenAusGeschlossenenKlassenErstellenAsync_ErstelltNurEineSonderabrechnungUndNichtDoppelt()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var closedCycle = db.KursNiveauDurchlaeufe.Single();
        closedCycle.StartDatum = DateTime.UtcNow.AddDays(-10);
        closedCycle.EndDatum = DateTime.UtcNow.AddDays(-2);
        db.LevelEnrollments.AddRange(
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = DateTime.UtcNow.AddDays(-5),
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m
            },
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = DateTime.UtcNow,
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m
            });
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        var (succeeded, _) = await service.SammelabrechnungenAusGeschlossenenKlassenErstellenAsync();
        var (secondSucceeded, _) = await service.SammelabrechnungenAusGeschlossenenKlassenErstellenAsync();

        Assert.True(succeeded);
        Assert.False(secondSucceeded);
        var periode = Assert.Single(db.Abrechnungsperioden);
        Assert.Equal(schule.Id, periode.SchulID);
        Assert.True(periode.IstSammelabrechnungAusGeschlossenenKlassen);
        Assert.Equal(11_000m, periode.Umsatz);
        Assert.Single(db.LevelEnrollments.Where(enrollment => enrollment.AbgerechnetInPeriodeId == periode.Id));
        Assert.Single(db.LevelEnrollments.Where(enrollment => enrollment.AbgerechnetInPeriodeId == null));
    }

    [Fact]
    public async Task GetUeberfaelligeForderungenAusAktivenKlassenAsync_LiefertNurSeitZehnMonatenOffeneAbosAktiverKlassen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var jetzt = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var activeCycle = db.KursNiveauDurchlaeufe.Single();
        activeCycle.StartDatum = jetzt.AddMonths(-11);

        db.LevelEnrollments.AddRange(
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = jetzt.AddMonths(-10),
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m
            },
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = jetzt.AddMonths(-10).AddTicks(1),
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m
            },
            new LevelEnrollment
            {
                Id = Guid.NewGuid(),
                StudentId = student.Id,
                KlasseId = level.Id,
                IsPaid = true,
                PaidAtUtc = jetzt.AddMonths(-10),
                PriceAtEnrollment = 12_500m,
                SchoolLeaderCommissionAtEnrollment = 500m,
                TeacherCommissionAtEnrollment = 1_000m,
                AbgerechnetInPeriodeId = Guid.NewGuid()
            });
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        var forderungen = await service.GetUeberfaelligeForderungenAusAktivenKlassenAsync(jetzt);

        var forderung = Assert.Single(forderungen);
        Assert.Equal(schule.Id, forderung.SchulID);
        Assert.Equal(level.Id, forderung.KlasseId);
        Assert.Equal(jetzt.AddMonths(-10), forderung.AeltesteZahlungAmUtc);
        Assert.Equal((int)(jetzt - jetzt.AddMonths(-10)).TotalDays, forderung.TageOffen);
        Assert.Equal(1, forderung.AnzahlAbonnements);
        Assert.Equal(11_000m, forderung.Plattformforderung);

        activeCycle.EndDatum = jetzt.AddDays(-1);
        await db.SaveChangesAsync();

        Assert.Empty(await service.GetUeberfaelligeForderungenAusAktivenKlassenAsync(jetzt));
    }

    [Fact]
    public async Task GetUeberfaelligeForderungenAusAktivenKlassenAsync_WarntNichtBeiJuengeremCycleOderAlterZahlungAusAnderemCycle()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var jetzt = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
        var activeCycle = db.KursNiveauDurchlaeufe.Single();
        activeCycle.StartDatum = jetzt.AddMonths(-9);
        var alterCycle = new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(), SchulID = schule.Id, KlasseId = level.Id,
            NiveauPreis = 12500m, StartDatum = jetzt.AddMonths(-14),
            EndDatum = jetzt.AddMonths(-11), ErstelltVonUserID = Guid.NewGuid()
        };
        db.KursNiveauDurchlaeufe.Add(alterCycle);
        var enrollment = new LevelEnrollment
        {
            Id = Guid.NewGuid(), StudentId = student.Id, KlasseId = level.Id,
            DurchlaufId = alterCycle.Id, IstAktuell = false, IsPaid = true,
            PaidAtUtc = jetzt.AddMonths(-12), PriceAtEnrollment = 12500m,
            SchoolLeaderCommissionAtEnrollment = 500m, TeacherCommissionAtEnrollment = 1000m
        };
        db.LevelEnrollments.Add(enrollment);
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        Assert.Empty(await service.GetUeberfaelligeForderungenAusAktivenKlassenAsync(jetzt));

        activeCycle.StartDatum = jetzt.AddMonths(-11);
        await db.SaveChangesAsync();
        Assert.Empty(await service.GetUeberfaelligeForderungenAusAktivenKlassenAsync(jetzt));

        enrollment.DurchlaufId = activeCycle.Id;
        await db.SaveChangesAsync();
        Assert.Single(await service.GetUeberfaelligeForderungenAusAktivenKlassenAsync(jetzt));
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_OrdnetPositionenZuUndSchreibtUmsatzFest()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);

        // Act
        var (succeeded, _) = await service.AbrechnungErstellenAsync(schule.Id);

        // Assert
        Assert.True(succeeded);
        var periode = Assert.Single(db.Abrechnungsperioden);
        Assert.Equal(AbrechnungStatus.Erstellt, periode.Status);
        Assert.False(periode.IstAbgeschlossen);
        Assert.Equal(
            SkoulouAppKonstanten.StandardPreisAbo
                - SkoulouAppKonstanten.ProvisionSchulleiterBasis
                - SkoulouAppKonstanten.ProvisionLehrerBasis,
            periode.Umsatz);
        Assert.Equal(1, periode.AnzahlAbonnements);

        var enrollment = db.LevelEnrollments.Single();
        Assert.Equal(periode.Id, enrollment.AbgerechnetInPeriodeId);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_ProduktOwnerDarfNurZugewieseneSchuleAbrechnen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var fremdeSchule = new School
        {
            Id = Guid.NewGuid(),
            Name = "Schule B",
            IsActive = true,
            ErstelltAmUtc = DateTime.UtcNow.AddYears(-1)
        };
        var produktOwner = new AppUser { Id = Guid.NewGuid(), FullName = "Product Owner", UserName = "owner" };
        var produktOwnerRole = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner, NormalizedName = "PRODUKTOWNER" };
        db.Schools.Add(fremdeSchule);
        db.Users.Add(produktOwner);
        db.Roles.Add(produktOwnerRole);
        db.UserRoles.Add(new AppUserRole { UserId = produktOwner.Id, RoleId = produktOwnerRole.Id });
        db.UserSchools.Add(new UserSchool { AppUserId = produktOwner.Id, SchoolId = schule.Id });
        await db.SaveChangesAsync();

        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);

        var erlaubt = await service.AbrechnungErstellenAsync(schule.Id, erstellerId: produktOwner.Id);
        var verweigert = await service.AbrechnungErstellenAsync(fremdeSchule.Id, erstellerId: produktOwner.Id);

        Assert.True(erlaubt.Succeeded, erlaubt.ErrorMessage);
        Assert.False(verweigert.Succeeded);
        Assert.Contains("autorisé", verweigert.ErrorMessage);
        var periode = Assert.Single(db.Abrechnungsperioden);
        var zahlung = await service.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: true, schulLeiterId: produktOwner.Id);
        Assert.False(zahlung.Succeeded);
        Assert.Equal(AbrechnungStatus.Erstellt, periode.Status);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_ErlaubtFreiGewaehltesEnddatumNachHeute()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var gewuenschtesEnde = DateTime.UtcNow.AddDays(14);
        var service = new AbrechnungService(db);

        // Act
        var (succeeded, _) = await service.AbrechnungErstellenAsync(schule.Id, gewuenschtesEnde);

        // Assert
        Assert.True(succeeded);
        var periode = Assert.Single(db.Abrechnungsperioden);
        Assert.Equal(gewuenschtesEnde, periode.BisUtc);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_IgnoriertAlteKursZahlungenOhneSkoulouAppProvisionen()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        db.LevelEnrollments.Add(new LevelEnrollment
        {
            StudentId = student.Id,
            KlasseId = level.Id,
            AboTyp = AboTyp.SkoulouApp,
            IsPaid = true,
            PaidAtUtc = DateTime.UtcNow.AddHours(-1),
            PriceAtEnrollment = 50000m,
            SchoolLeaderCommissionAtEnrollment = 0m,
            TeacherCommissionAtEnrollment = 0m
        });
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        // Act
        var uebersicht = await service.GetOffeneUebersichtAsync(schule.Id);
        var (succeeded, message) = await service.AbrechnungErstellenAsync(schule.Id);

        // Assert
        Assert.Equal(0, uebersicht.AnzahlPositionen);
        Assert.Equal(0m, uebersicht.OffenerUmsatz);
        Assert.False(succeeded);
        Assert.Contains("abonnement SKOULOUAPP", message);
    }

    [Fact]
    public async Task Workflow_AkzeptierenUndBestaetigen_SchliesstAbrechnungAb()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);
        var periode = db.Abrechnungsperioden.Single();
        var superAdminId = await TestIdentityFactory.AddSuperAdminAsync(db);

        // Act: SchulLeiter akzeptiert, SuperAdmin bestätigt
        var (akzeptiertOk, _) = await service.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: true);
        var (bestaetigtOk, _) = await service.AdminBestaetigenAsync(periode.Id, superAdminId);

        // Assert
        Assert.True(akzeptiertOk);
        Assert.True(bestaetigtOk);
        var aktualisiert = db.Abrechnungsperioden.Single();
        Assert.Equal(AbrechnungStatus.Bestaetigt, aktualisiert.Status);
        Assert.True(aktualisiert.IstAbgeschlossen);
        Assert.Equal(superAdminId, aktualisiert.BestaetigtVonSuperAdminId);
    }

    [Fact]
    public async Task AdminBestaetigenAsync_FremderBenutzerKannKeineZahlungAbschliessen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var schoolLeaderId = await TestIdentityFactory.AddSchoolLeaderAsync(db, schule.Id);
        var fremderBenutzerId = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = fremderBenutzerId, FullName = "Fremder Benutzer", UserName = "fremd" });
        await db.SaveChangesAsync();
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);
        var periode = Assert.Single(db.Abrechnungsperioden);
        Assert.True((await service.SchulLeiterStatusSetzenAsync(periode.Id, true, schulLeiterId: schoolLeaderId)).Succeeded);

        var result = await service.AdminBestaetigenAsync(periode.Id, fremderBenutzerId);

        Assert.False(result.Succeeded);
        Assert.Equal(AbrechnungStatus.Akzeptiert, db.Abrechnungsperioden.Single().Status);
    }

    [Fact]
    public async Task GetOffeneSuperAdminBestaetigungenAsync_LiefertErstellerUndZahlungsmeldenden()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var produktOwner = new AppUser { Id = Guid.NewGuid(), FullName = "Product Owner", UserName = "owner" };
        var produktOwnerRole = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner, NormalizedName = "PRODUKTOWNER" };
        db.Users.Add(produktOwner);
        db.Roles.Add(produktOwnerRole);
        db.UserRoles.Add(new AppUserRole { UserId = produktOwner.Id, RoleId = produktOwnerRole.Id });
        db.UserSchools.Add(new UserSchool { AppUserId = produktOwner.Id, SchoolId = schule.Id, CanManageStudents = true });
        var schoolLeaderId = await TestIdentityFactory.AddSchoolLeaderAsync(db, schule.Id);
        await db.SaveChangesAsync();

        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        Assert.True((await service.AbrechnungErstellenAsync(schule.Id, erstellerId: produktOwner.Id)).Succeeded);
        var periode = Assert.Single(db.Abrechnungsperioden);
        Assert.True((await service.SchulLeiterStatusSetzenAsync(periode.Id, true, schulLeiterId: schoolLeaderId)).Succeeded);

        var bestaetigung = Assert.Single(await service.GetOffeneSuperAdminBestaetigungenAsync());

        Assert.Equal(periode.Id, bestaetigung.AbrechnungsperiodeId);
        Assert.Equal("Product Owner", bestaetigung.RechnungErstelltVon);
        Assert.Equal("Directeur", bestaetigung.ZahlungGemeldetVon);
        Assert.True(bestaetigung.WurdeVonProduktOwnerErstellt);
    }

    [Fact]
    public async Task SchulLeiterStatusSetzenAsync_AblehnenOhneGrund_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);
        var periode = db.Abrechnungsperioden.Single();

        // Act
        var (succeeded, message) = await service.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: false, ablehnungsGrund: "  ");

        // Assert
        Assert.False(succeeded);
        Assert.Contains("motif", message);
    }

    [Fact]
    public async Task SchulLeiterStatusSetzenAsync_FremdeSchule_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var fremdeSchule = new School { Id = Guid.NewGuid(), Name = "Fremde Schule", IsActive = true };
        var directorId = Guid.NewGuid();
        db.Schools.Add(fremdeSchule);
        db.Users.Add(new AppUser { Id = directorId, UserName = "director" });
        db.UserSchools.Add(new UserSchool { AppUserId = directorId, SchoolId = fremdeSchule.Id, CanManageStudents = true });
        await db.SaveChangesAsync();

        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);
        var periode = db.Abrechnungsperioden.Single();

        // Act
        var (succeeded, message) = await service.SchulLeiterStatusSetzenAsync(periode.Id, akzeptiert: true, schulLeiterId: directorId);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("autorisé à modifier", message);
        Assert.Equal(AbrechnungStatus.Erstellt, db.Abrechnungsperioden.Single().Status);
    }

    [Fact]
    public async Task SchulLeiterStatusSetzenAsync_KoordinatorBrauchtBillingRechtDerRechnungsschule()
    {
        using var db = TestDbContext.CreateInMemory();
        var (schule, _, _) = SeedBasis(db);
        var fremdeSchule = new School { Id = Guid.NewGuid(), Name = "Autre école", IsActive = true };
        var koordinator = new AppUser { Id = Guid.NewGuid(), UserName = "coordinateur" };
        var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Koordinator };
        var link = new UserSchool
        {
            AppUserId = koordinator.Id, SchoolId = schule.Id, CanManageStudents = true,
            CanAccessFinances = false, CanManageCyclePayments = true
        };
        db.Schools.Add(fremdeSchule);
        db.Users.Add(koordinator);
        db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { UserId = koordinator.Id, RoleId = rolle.Id });
        db.UserSchools.Add(link);
        var periode = new Abrechnungsperiode { SchulID = schule.Id, Status = AbrechnungStatus.Erstellt };
        var fremdePeriode = new Abrechnungsperiode { SchulID = fremdeSchule.Id, Status = AbrechnungStatus.Erstellt };
        db.Abrechnungsperioden.AddRange(periode, fremdePeriode);
        await db.SaveChangesAsync();
        var service = new AbrechnungService(db);

        Assert.False((await service.SchulLeiterStatusSetzenAsync(periode.Id, true, schulLeiterId: koordinator.Id)).Succeeded);
        link.CanAccessFinances = true;
        link.CanManageCyclePayments = false;
        await db.SaveChangesAsync();
        Assert.False((await service.SchulLeiterStatusSetzenAsync(periode.Id, true, schulLeiterId: koordinator.Id)).Succeeded);
        link.CanAccessBilling = true;
        await db.SaveChangesAsync();
        Assert.False((await service.SchulLeiterStatusSetzenAsync(fremdePeriode.Id, true, schulLeiterId: koordinator.Id)).Succeeded);

        Assert.True((await service.SchulLeiterStatusSetzenAsync(periode.Id, false, "À vérifier", koordinator.Id)).Succeeded);
        Assert.True((await service.SchulLeiterStatusSetzenAsync(periode.Id, true, schulLeiterId: koordinator.Id)).Succeeded);
        Assert.Equal(AbrechnungStatus.Akzeptiert, periode.Status);
        Assert.Equal(koordinator.Id, periode.ZahlungGemeldetVonUserId);
        Assert.Equal(AbrechnungStatus.Erstellt, fremdePeriode.Status);
    }

    [Fact]
    public async Task AdminBestaetigenAsync_OhneAkzeptierung_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);
        var periode = db.Abrechnungsperioden.Single();
        var superAdminId = await TestIdentityFactory.AddSuperAdminAsync(db);

        // Act
        var (succeeded, _) = await service.AdminBestaetigenAsync(periode.Id, superAdminId);

        // Assert
        Assert.False(succeeded);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_OhneOffenePositionen_SchlaegtFehl()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, _, _) = SeedBasis(db);
        var service = new AbrechnungService(db);

        // Act
        var (succeeded, message) = await service.AbrechnungErstellenAsync(schule.Id);

        // Assert
        Assert.False(succeeded);
        Assert.Contains("abonnement SKOULOUAPP", message);
    }

    [Fact]
    public async Task AbrechnungErstellenAsync_ZweiteErstellung_ZaehltPositionenNichtDoppelt()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);

        // Act
        var (succeeded, _) = await service.AbrechnungErstellenAsync(schule.Id);

        // Assert
        Assert.False(succeeded);
        Assert.Single(db.Abrechnungsperioden);
    }

    [Fact]
    public async Task GetHistorieAsync_LiefertErstelltePerioden()
    {
        // Arrange
        using var db = TestDbContext.CreateInMemory();
        var (schule, student, level) = SeedBasis(db);
        var abo = new AboService(db);
        await abo.AboZuweisenAsync(student.Id, level.Id, AboTyp.SkoulouApp);
        await abo.BezahltSetzenAsync(student.Id, level.Id);
        var service = new AbrechnungService(db);
        await service.AbrechnungErstellenAsync(schule.Id);

        // Act
        var historie = await service.GetHistorieAsync(schule.Id);

        // Assert
        Assert.Single(historie);
    }
}
