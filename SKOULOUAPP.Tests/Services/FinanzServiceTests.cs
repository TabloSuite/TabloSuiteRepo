using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Finanzen;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using Xunit;

namespace HalloApp.Tests.Services;

/// <summary>
/// Tests für <see cref="FinanzService"/>: Kategorien, manuelle Buchungen,
/// Löschregeln, KursNiveau-Durchläufe (Start/Ende) und Ratenzahlungen.
/// </summary>
public class FinanzServiceTests
{
    private static readonly byte[] TestPdf = "%PDF-1.4\nTest"u8.ToArray();
    [Theory]
    [InlineData(-1, FinanzSaldoStatus.Verlust)]
    [InlineData(0, FinanzSaldoStatus.Ausgeglichen)]
    [InlineData(1, FinanzSaldoStatus.Gewinn)]
    [InlineData(49_999, FinanzSaldoStatus.Gewinn)]
    public void BestimmeSaldoStatus_RichtetSichNurNachDemVorzeichen(int saldo, FinanzSaldoStatus erwartet)
    {
        Assert.Equal(erwartet, DirektorKontostandUebersicht.BestimmeSaldoStatus(saldo));
    }

    [Fact]
    public async Task GetSchulenAsync_SchulLeiterSiehtNurEigeneSchulen()
    {
        using var db = TestDbContext.CreateInMemory();
        var directorId = Guid.NewGuid();
        var ownSchool = SeedSchule(db, "École du directeur");
        var foreignSchool = SeedSchule(db, "École étrangère");
        db.UserSchools.Add(new UserSchool
        {
            AppUserId = directorId,
            SchoolId = ownSchool.Id,
            CanManageStudents = true
        });
        var directorRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        db.Roles.Add(directorRole);
        db.UserRoles.Add(new AppUserRole { UserId = directorId, RoleId = directorRole.Id });
        await db.SaveChangesAsync();

        var service = new FinanzService(db);

        var schools = await service.GetSchulenAsync(directorId, istSuperAdmin: false);

        var school = Assert.Single(schools);
        Assert.Equal(ownSchool.Id, school.Id);
    }

    [Fact]
    public async Task BuchungErfassenAsync_SchulLeiterKannKeineFremdeSchuleVerwalten()
    {
        using var db = TestDbContext.CreateInMemory();
        var directorId = Guid.NewGuid();
        var ownSchool = SeedSchule(db, "École du directeur");
        var foreignSchool = SeedSchule(db, "École étrangère");
        var category = new FinanzKategorie
        {
            Id = Guid.NewGuid(),
            SchulID = foreignSchool.Id,
            Bezeichnung = "Frais",
            Richtung = FinanzRichtung.Einnahme,
            IstAktiv = true
        };
        db.UserSchools.Add(new UserSchool
        {
            AppUserId = directorId,
            SchoolId = ownSchool.Id,
            CanManageStudents = true
        });
        db.FinanzKategorien.Add(category);
        await db.SaveChangesAsync();

        var service = new FinanzService(db);

        var (succeeded, error, booking) = await service.BuchungErfassenAsync(
            foreignSchool.Id,
            category.Id,
            1000m,
            DateTime.Today,
            directorId,
            autorisierungErzwingen: true);

        Assert.False(succeeded);
        Assert.Null(booking);
        Assert.Contains("pas autorisé", error);
    }

    private static School SeedSchule(TestDbContext db, string name = "Schule A")
    {
        var schule = new School
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = true,
            ErstelltAmUtc = DateTime.UtcNow.AddYears(-1)
        };
        db.Schools.Add(schule);
        db.SaveChanges();
        return schule;
    }

    private static (Klasse Level, AppUser Student) SeedNiveauMitSchueler(
        TestDbContext db, School schule, decimal preis = 12500m)
    {
        var level = new Klasse
        {
            Id = Guid.NewGuid(),
            Code = "A1.1",
            Title = "Anfänger",
            Price = preis,
            SchoolId = schule.Id,
            IstSystemNiveau = false
        };
        var studentUser = new AppUser { Id = Guid.NewGuid(), FullName = "Schüler", UserName = "schueler" };
        var profil = new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = studentUser.Id,
            SchoolId = schule.Id,
            CurrentKlasseId = level.Id,
            CurrentLevelCode = level.Code
        };
        var enrollment = new LevelEnrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentUser.Id,
            KlasseId = level.Id
        };

        db.Klassen.Add(level);
        db.Users.Add(studentUser);
        db.StudentProfiles.Add(profil);
        db.LevelEnrollments.Add(enrollment);
        db.SaveChanges();

        return (level, studentUser);
    }

    private static async Task<Guid> SeedSchulLeiterAsync(TestDbContext db, School schule)
    {
        var directorId = Guid.NewGuid();
        var role = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter, NormalizedName = Rollen.SchulLeiter.ToUpperInvariant() };
        db.Roles.Add(role);
        db.Users.Add(new AppUser { Id = directorId, UserName = "director" });
        db.UserRoles.Add(new AppUserRole { UserId = directorId, RoleId = role.Id });
        db.UserSchools.Add(new UserSchool { AppUserId = directorId, SchoolId = schule.Id, CanManageStudents = true });
        await db.SaveChangesAsync();
        return directorId;
    }

    private static async Task<Guid> SeedKoordinatorAsync(TestDbContext db, School schule)
    {
        var coordinatorId = Guid.NewGuid();
        var role = new Role { Id = Guid.NewGuid(), Name = Rollen.Koordinator, NormalizedName = Rollen.Koordinator.ToUpperInvariant() };
        db.Roles.Add(role);
        db.Users.Add(new AppUser { Id = coordinatorId, UserName = "coordinator" });
        db.UserRoles.Add(new AppUserRole { UserId = coordinatorId, RoleId = role.Id });
        db.UserSchools.Add(new UserSchool { AppUserId = coordinatorId, SchoolId = schule.Id, CanManageStudents = true });
        await db.SaveChangesAsync();
        return coordinatorId;
    }

    [Fact]
    public async Task GetFinanzBerichtAsync_BeschraenktSchuleUndFilterAuchBeiManipulierterUrl()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = SeedSchule(db, "École autorisée");
        var other = SeedSchule(db, "École étrangère");
        var director = await SeedSchulLeiterAsync(db, school);
        var coordinator = await SeedKoordinatorAsync(db, school);
        var service = new FinanzService(db);
        var date = new DateTime(2026, 10, 1);
        foreach (var item in new[] { (school, date, 100m), (school, date.AddDays(1).AddHours(12), 200m), (school, date.AddDays(2), 300m), (other, date, 400m) })
        {
            var category = new FinanzKategorie { Id = Guid.NewGuid(), SchulID = item.Item1.Id, Bezeichnung = "Scolarité", Richtung = FinanzRichtung.Einnahme };
            db.FinanzKategorien.Add(category);
            db.FinanzBuchungen.Add(new FinanzBuchung { Id = Guid.NewGuid(), SchulID = item.Item1.Id, Schule = item.Item1, KategorieID = category.Id, Kategorie = category, ErfasstVonUserID = director, Richtung = FinanzRichtung.Einnahme, Betrag = item.Item3, Wirkungsdatum = item.Item2 });
        }
        await db.SaveChangesAsync();

        var filter = new FinanzFilter { SchulID = school.Id, Von = date.AddDays(1), Bis = date.AddDays(1), Richtung = FinanzRichtung.Einnahme };
        Assert.Null(await service.GetFinanzBerichtAsync(coordinator, false, filter));
        Assert.True((await service.SetCoordinatorFinanceAccessAsync(director, coordinator, school.Id, true, true, false, false)).Succeeded);
        var report = await service.GetFinanzBerichtAsync(coordinator, false, filter);
        Assert.NotNull(report);
        Assert.Equal("École autorisée", report.SchulName);
        Assert.Equal(200m, report.Uebersicht.SummeEinnahmen);
        Assert.Single(report.Uebersicht.Zeilen);
        Assert.Null(await service.GetFinanzBerichtAsync(coordinator, false, new FinanzFilter { SchulID = other.Id }));
        Assert.Null(await service.GetFinanzBerichtAsync(coordinator, false, new FinanzFilter { SchulID = school.Id, Von = date.AddDays(2), Bis = date }));
    }

    [Fact]
    public async Task GetFinanceAccessAsync_KoordinatorSiehtNurFreigegebenenDashboardTab()
    {
        using var db = TestDbContext.CreateInMemory();
        var school = SeedSchule(db);
        var directorId = await SeedSchulLeiterAsync(db, school);
        var coordinatorId = await SeedKoordinatorAsync(db, school);
        var service = new FinanzService(db);

        Assert.Equal(default, await service.GetFinanceAccessAsync(coordinatorId, school.Id));
        var granted = await service.SetCoordinatorFinanceAccessAsync(directorId, coordinatorId, school.Id,
            menu: true, dashboard: true, entries: false, cycles: false);
        Assert.True(granted.Succeeded, granted.Message);

        var access = await service.GetFinanceAccessAsync(coordinatorId, school.Id);
        Assert.True(access.Menu);
        Assert.True(access.Dashboard);
        Assert.False(access.Entries);
        Assert.False(access.Cycles);
        Assert.Single(await service.GetSchulenAsync(coordinatorId, false));
    }

    // -------------------- Kategorien --------------------

    [Fact]
    public async Task KategorieAnlegenAsync_LegtSchulLokaleKategorieAn()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var service = new FinanzService(db);

        var (succeeded, _, kategorie) = await service.KategorieAnlegenAsync(
            schule.Id, "Spende", FinanzRichtung.Einnahme);

        Assert.True(succeeded);
        Assert.NotNull(kategorie);
        Assert.Equal(schule.Id, kategorie!.SchulID);
        Assert.Equal(FinanzRichtung.Einnahme, kategorie.Richtung);
        Assert.False(kategorie.IstSystemTyp);
    }

    [Fact]
    public async Task KategorieAnlegenAsync_LehntLeereBezeichnungAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var service = new FinanzService(db);

        var (succeeded, error, kategorie) = await service.KategorieAnlegenAsync(
            schule.Id, "   ", FinanzRichtung.Ausgabe);

        Assert.False(succeeded);
        Assert.Null(kategorie);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task DurchlaufStartenAsync_LehntGlobalesSystemNiveauAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var systemNiveau = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "Standard", IstSystemNiveau = true };
        db.Klassen.Add(systemNiveau);
        await db.SaveChangesAsync();
        var service = new FinanzService(db);

        var (succeeded, error, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, systemNiveau.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        Assert.False(succeeded);
        Assert.NotEmpty(error);
        Assert.Null(durchlauf);
    }

    [Fact]
    public async Task KategorieAnlegenAsync_LehntDuplikatAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var service = new FinanzService(db);
        await service.KategorieAnlegenAsync(schule.Id, "Miete", FinanzRichtung.Ausgabe);

        var (succeeded, _, _) = await service.KategorieAnlegenAsync(
            schule.Id, "Miete", FinanzRichtung.Ausgabe);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task KategorieLoeschenAsync_LoeschtFreieKategorieUndSchuetztVerwendeteKategorie()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var leiterId = await SeedSchulLeiterAsync(db, schule);
        var service = new FinanzService(db, new FakeFinanzBelegStorage());
        var (_, _, freieKategorie) = await service.KategorieAnlegenAsync(schule.Id, "Vente de scripts", FinanzRichtung.Einnahme);
        var (_, _, verwendeteKategorie) = await service.KategorieAnlegenAsync(schule.Id, "Matériel", FinanzRichtung.Ausgabe);
        await service.BuchungErfassenAsync(schule.Id, verwendeteKategorie!.Id, 1_000m, DateTime.UtcNow, leiterId, belegnummer: "REF-01", dateiname: "facture.pdf", belegInhalt: TestPdf);

        var freieKategorieErgebnis = await service.KategorieLoeschenAsync(freieKategorie!.Id, leiterId, istSuperAdmin: false);
        var verwendeteKategorieErgebnis = await service.KategorieLoeschenAsync(verwendeteKategorie.Id, leiterId, istSuperAdmin: false);

        Assert.True(freieKategorieErgebnis.Succeeded, freieKategorieErgebnis.ErrorMessage);
        Assert.False(verwendeteKategorieErgebnis.Succeeded);
        Assert.Contains("écritures", verwendeteKategorieErgebnis.ErrorMessage);
        Assert.DoesNotContain(db.FinanzKategorien, kategorie => kategorie.Id == freieKategorie.Id);
        Assert.Contains(db.FinanzKategorien, kategorie => kategorie.Id == verwendeteKategorie.Id);
    }

    [Fact]
    public async Task GetKategorienAsync_LiefertGlobaleUndSchulEigene()
    {
        using var db = TestDbContext.CreateInMemory();
        var schuleA = SeedSchule(db, "Schule A");
        var schuleB = SeedSchule(db, "Schule B");
        db.FinanzKategorien.Add(new FinanzKategorie
        {
            SchulID = null,
            Bezeichnung = "Global",
            Richtung = FinanzRichtung.Einnahme,
            IstAktiv = true
        });
        db.FinanzKategorien.Add(new FinanzKategorie
        {
            SchulID = schuleB.Id,
            Bezeichnung = "Nur B",
            Richtung = FinanzRichtung.Einnahme,
            IstAktiv = true
        });
        db.SaveChanges();
        var service = new FinanzService(db);
        await service.KategorieAnlegenAsync(schuleA.Id, "Nur A", FinanzRichtung.Einnahme);

        var kategorien = await service.GetKategorienAsync(schuleA.Id);

        Assert.Contains(kategorien, x => x.Bezeichnung == "Global");
        Assert.Contains(kategorien, x => x.Bezeichnung == "Nur A");
        Assert.DoesNotContain(kategorien, x => x.Bezeichnung == "Nur B");
    }

    // -------------------- Buchungen --------------------

    [Fact]
    public async Task BuchungErfassenAsync_SchreibtBuchungMitRichtungAusKategorie()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var storage = new FakeFinanzBelegStorage();
        var service = new FinanzService(db, storage);
        var (_, _, kategorie) = await service.KategorieAnlegenAsync(schule.Id, "Miete", FinanzRichtung.Ausgabe);
        var user = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = user, UserName = "buchhalter" });
        await db.SaveChangesAsync();

        var (succeeded, _, buchung) = await service.BuchungErfassenAsync(
            schule.Id, kategorie!.Id, 5000m, DateTime.UtcNow.AddDays(-3), user, belegnummer: " FAC-42 ", dateiname: "facture.pdf", belegInhalt: TestPdf);

        Assert.True(succeeded);
        Assert.NotNull(buchung);
        Assert.Equal(FinanzRichtung.Ausgabe, buchung!.Richtung);
        Assert.Equal(5000m, buchung.Betrag);
        Assert.Equal(user, buchung.ErfasstVonUserID);
        Assert.Equal("FAC-42", buchung.Belegnummer);
        Assert.Equal($"B-{buchung.Id:N}".ToUpperInvariant(), buchung.Buchungsnummer);
        var journal = await service.GetUebersichtAsync(new FinanzFilter { SchulID = schule.Id });
        Assert.Equal(buchung.Buchungsnummer, Assert.Single(journal.Zeilen).Buchungsnummer);
        Assert.True(Assert.Single(journal.Zeilen).HatBelegdatei);
        Assert.Equal("facture.pdf", Assert.Single(journal.Zeilen).BelegDateiname);
        var beleg = Assert.Single(db.FinanzBelege);
        Assert.Empty(beleg.Inhalt);
        Assert.Equal(TestPdf, storage.Dateien[beleg.BlobName!]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task BuchungErfassenAsync_ErlaubtBuchungOhneReferenzUndDatei(string? belegnummer)
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var storage = new FakeFinanzBelegStorage();
        var service = new FinanzService(db, storage);
        var (_, _, kategorie) = await service.KategorieAnlegenAsync(schule.Id, "Frais", FinanzRichtung.Ausgabe);
        var user = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = user, UserName = "comptable" });
        await db.SaveChangesAsync();

        var (ok, _, buchung) = await service.BuchungErfassenAsync(
            schule.Id, kategorie!.Id, 100m, DateTime.Today, user, belegnummer: belegnummer);

        Assert.True(ok);
        Assert.NotNull(buchung);
        Assert.Null(buchung.Belegnummer);
        Assert.Empty(db.FinanzBelege);
        var zeile = Assert.Single((await service.GetUebersichtAsync(new FinanzFilter { SchulID = schule.Id })).Zeilen);
        Assert.False(zeile.HatBelegdatei);
        Assert.Null(zeile.BelegDateiname);
    }

    [Theory]
    [InlineData("facture.pdf", "texte")]
    [InlineData("facture.exe", "%PDF-1.4")]
    [InlineData(null, "%PDF-1.4")]
    public async Task BuchungErfassenAsync_LehntUngueltigeDateiAb(string? dateiname, string inhalt)
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var service = new FinanzService(db);
        var (_, _, kategorie) = await service.KategorieAnlegenAsync(schule.Id, "Frais", FinanzRichtung.Ausgabe);
        var (ok, _, _) = await service.BuchungErfassenAsync(schule.Id, kategorie!.Id, 100m,
            DateTime.Today, Guid.NewGuid(), belegnummer: "REF", dateiname: dateiname,
            belegInhalt: System.Text.Encoding.UTF8.GetBytes(inhalt));
        Assert.False(ok);
        Assert.Empty(db.FinanzBelege);
    }

    [Fact]
    public async Task GetFinanzBelegAsync_IsoliertSchulenUndBerechtigungen()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var fremdeSchule = SeedSchule(db, "Autre école");
        var autor = await SeedSchulLeiterAsync(db, schule);
        var fremd = await SeedSchulLeiterAsync(db, fremdeSchule);
        var service = new FinanzService(db, new FakeFinanzBelegStorage());
        var (_, _, kategorie) = await service.KategorieAnlegenAsync(schule.Id, "Frais", FinanzRichtung.Ausgabe);
        var (ok, _, buchung) = await service.BuchungErfassenAsync(schule.Id, kategorie!.Id, 100m,
            DateTime.Today, autor, belegnummer: "FAC", dateiname: "facture.pdf", belegInhalt: TestPdf);
        Assert.True(ok);
        Assert.NotNull(await service.GetFinanzBelegAsync(buchung!.Id, autor));
        Assert.Null(await service.GetFinanzBelegAsync(buchung.Id, fremd));
        Assert.Null(await service.GetFinanzBelegAsync(buchung.Id, Guid.Empty));
        var beleg = await service.GetFinanzBelegAsync(buchung.Id, autor);
        await using var stream = (await service.OeffneFinanzBelegAsync(beleg!))!;
        using var bytes = new MemoryStream();
        await stream.CopyToAsync(bytes);
        Assert.Equal(TestPdf, bytes.ToArray());
    }

    [Fact]
    public async Task BuchungErfassenAsync_LehntManuelleHalloAppSystemKategorieAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var kategorie = new FinanzKategorie
        {
            Id = Guid.NewGuid(),
            Bezeichnung = "HALLOAPP-Abo · Part plateforme",
            Richtung = FinanzRichtung.Ausgabe,
            SystemCode = FinanzSystemCode.HalloAppAboAnteil,
            IstSystemTyp = true,
            IstAktiv = true
        };
        db.FinanzKategorien.Add(kategorie);
        await db.SaveChangesAsync();
        var service = new FinanzService(db);

        var (succeeded, error, buchung) = await service.BuchungErfassenAsync(
            schule.Id, kategorie.Id, 1000m, DateTime.UtcNow, Guid.NewGuid(), belegnummer: "REF-02", dateiname: "facture.pdf", belegInhalt: TestPdf);

        Assert.False(succeeded);
        Assert.Contains("automatiques", error);
        Assert.Null(buchung);
        Assert.Empty(db.FinanzBuchungen);
    }

    [Fact]
    public async Task BuchungErfassenAsync_LehntBetragKleinerGleichNullAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var service = new FinanzService(db);
        var (_, _, kategorie) = await service.KategorieAnlegenAsync(schule.Id, "Miete", FinanzRichtung.Ausgabe);

        var (succeeded, _, _) = await service.BuchungErfassenAsync(
            schule.Id, kategorie!.Id, 0m, DateTime.UtcNow, Guid.NewGuid());

        Assert.False(succeeded);
    }

    [Fact]
    public async Task BuchungLoeschenAsync_ErlaubtErstellerAberNichtFremde()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var storage = new FakeFinanzBelegStorage();
        var service = new FinanzService(db, storage);
        var (_, _, kategorie) = await service.KategorieAnlegenAsync(schule.Id, "Sonstiges", FinanzRichtung.Einnahme);
        var ersteller = Guid.NewGuid();
        db.UserSchools.Add(new UserSchool { AppUserId = ersteller, SchoolId = schule.Id, CanManageStudents = true });
        var directorRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        db.Roles.Add(directorRole);
        db.UserRoles.Add(new AppUserRole { UserId = ersteller, RoleId = directorRole.Id });
        await db.SaveChangesAsync();
        var (_, _, buchung) = await service.BuchungErfassenAsync(
            schule.Id, kategorie!.Id, 1000m, DateTime.UtcNow, ersteller, belegnummer: "REF-03", dateiname: "facture.pdf", belegInhalt: TestPdf);

        var fremd = await service.BuchungLoeschenAsync(buchung!.Id, Guid.NewGuid(), istSuperAdmin: false);
        Assert.False(fremd.Succeeded);

        var eigen = await service.BuchungLoeschenAsync(buchung.Id, ersteller, istSuperAdmin: false);
        Assert.True(eigen.Succeeded);
        Assert.Empty(db.FinanzBuchungen);
        Assert.Empty(storage.Dateien);
    }

    [Fact]
    public async Task HalloAppAboBuchungenErstellenAsync_BuchtVollenPreisAlsEingangUndPlattformLehrerAlsAusgang()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.IsPaid = true;
        enrollment.PaidAtUtc = DateTime.UtcNow;
        enrollment.AboTyp = AboTyp.HalloPlus;
        enrollment.PriceAtEnrollment = 20000m;
        enrollment.SchoolLeaderCommissionAtEnrollment = 800m;
        enrollment.TeacherCommissionAtEnrollment = 1600m;
        enrollment.ProductOwnerCommissionAtEnrollment = 400m;
        await db.SaveChangesAsync();
        var service = new FinanzService(db, new FakeFinanzBelegStorage());
        var erfasstVon = Guid.NewGuid();

        var result = await service.HalloAppAboBuchungenErstellenAsync(enrollment.Id, erfasstVon);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var buchungen = db.FinanzBuchungen.OrderBy(buchung => buchung.Betrag).ToList();
        Assert.Equal(3, buchungen.Count);
        Assert.Equal(3, buchungen.Select(buchung => buchung.Buchungsnummer).Distinct().Count());
        Assert.All(buchungen, buchung => Assert.Equal($"B-{buchung.Id:N}".ToUpperInvariant(), buchung.Buchungsnummer));
        Assert.Contains(buchungen, buchung => buchung.Richtung == FinanzRichtung.Einnahme && buchung.Betrag == 20000m);
        Assert.Contains(buchungen, buchung => buchung.Richtung == FinanzRichtung.Ausgabe && buchung.Betrag == 17600m);
        Assert.Contains(buchungen, buchung => buchung.Richtung == FinanzRichtung.Ausgabe && buchung.Betrag == 1600m);
        Assert.DoesNotContain(buchungen, buchung => buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboProductOwnerAnteil);
        Assert.All(buchungen, buchung =>
        {
            Assert.Equal(schule.Id, buchung.SchulID);
            Assert.Equal(enrollment.Id, buchung.HerkunftReferenzID);
            Assert.Equal(erfasstVon, buchung.ErfasstVonUserID);
        });
    }

    [Fact]
    public async Task GetDirektorKontostandUebersichtAsync_TrenntSchulbewegungenUndAboAnteileVomVerfuegbarenSaldo()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (_, _) = SeedNiveauMitSchueler(db, schule);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.IsPaid = true;
        enrollment.PaidAtUtc = DateTime.UtcNow;
        enrollment.PriceAtEnrollment = 20_000m;
        enrollment.SchoolLeaderCommissionAtEnrollment = 800m;
        enrollment.TeacherCommissionAtEnrollment = 1_600m;
        enrollment.ProductOwnerCommissionAtEnrollment = 400m;
        await db.SaveChangesAsync();

        var service = new FinanzService(db);
        var erfasstVon = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = erfasstVon, UserName = "buchhalter" });
        await db.SaveChangesAsync();
        var aboBuchung = await service.HalloAppAboBuchungenErstellenAsync(enrollment.Id, erfasstVon);
        Assert.True(aboBuchung.Succeeded, aboBuchung.ErrorMessage);
        Assert.Contains(db.FinanzBuchungen, buchung => buchung.Kategorie.SystemCode == FinanzSystemCode.HalloAppAboZahlung);
        var (einnahmeOk, _, einnahmeKategorie) = await service.KategorieAnlegenAsync(schule.Id, "Don", FinanzRichtung.Einnahme);
        var (ausgabeOk, _, ausgabeKategorie) = await service.KategorieAnlegenAsync(schule.Id, "Matériel", FinanzRichtung.Ausgabe);
        Assert.True(einnahmeOk);
        Assert.True(ausgabeOk);
        await service.BuchungErfassenAsync(schule.Id, einnahmeKategorie!.Id, 5_000m, DateTime.Today, erfasstVon, belegnummer: "REF-04", dateiname: "facture.pdf", belegInhalt: TestPdf);
        await service.BuchungErfassenAsync(schule.Id, ausgabeKategorie!.Id, 2_000m, DateTime.Today, erfasstVon, belegnummer: "REF-05", dateiname: "facture.pdf", belegInhalt: TestPdf);

        var journal = await service.GetUebersichtAsync(new FinanzFilter { SchulID = schule.Id });
        Assert.Contains(journal.Zeilen, zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboZahlung);
        Assert.Equal(25_000m, journal.SummeEinnahmen);
        Assert.Equal(21_200m, journal.SummeAusgaben);
        Assert.Equal(3_800m, journal.Saldo);
        Assert.Equal(17_600m, journal.Zeilen
            .Where(zeile => zeile.KategorieSystemCode == FinanzSystemCode.HalloAppAboAnteil)
            .Sum(zeile => zeile.Betrag));
        var ausgabenJournal = await service.GetUebersichtAsync(new FinanzFilter
        {
            SchulID = schule.Id,
            Richtung = FinanzRichtung.Ausgabe
        });
        Assert.All(ausgabenJournal.Zeilen, zeile => Assert.Equal(FinanzRichtung.Ausgabe, zeile.Richtung));

        var uebersicht = await service.GetDirektorKontostandUebersichtAsync(
            schule.Id,
            new FinanzFilter { Richtung = FinanzRichtung.Ausgabe });

        Assert.Equal(20_000m, uebersicht.HalloAppAboEingenommen);
        Assert.Equal(800m, uebersicht.SchulleiterAboAnteil);
        Assert.Equal(1_600m, uebersicht.LehrerAboAnteil);
        Assert.Equal(17_600m, uebersicht.HalloAppAboAnteil);
        Assert.Equal(5_000m, uebersicht.SonstigeEinnahmen);
        Assert.Equal(2_000m, uebersicht.BetrieblicheAusgaben);
        Assert.Equal(3_800m, uebersicht.IstKontostand);
        Assert.Equal(3_800m, uebersicht.VerfuegbarerKontostand);
        Assert.Equal(23_000m, uebersicht.KontostandMitNichtUeberwiesenenHalloAppAnteilen);
        Assert.Equal(5_400m, uebersicht.WirklicherKontostandNachAboVerteilung);
    }

    // -------------------- Durchläufe --------------------

    [Fact]
    public async Task DurchlaufStartenAsync_ErzeugtAktivenDurchlauf()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, _) = SeedNiveauMitSchueler(db, schule);
        var service = new FinanzService(db);

        var (succeeded, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        Assert.True(succeeded);
        Assert.NotNull(durchlauf);
        Assert.True(durchlauf!.IstAktiv);
        Assert.Null(durchlauf.EndDatum);
    }

    [Fact]
    public async Task DurchlaufStartenAsync_VerhindertZweitenAktivenDurchlauf()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, _) = SeedNiveauMitSchueler(db, schule);
        var service = new FinanzService(db);
        await service.DurchlaufStartenAsync(schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        var (succeeded, error, _) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        Assert.False(succeeded);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task DurchlaufBeendenAsync_MeldetOffeneSchuldZurueck()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, _) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow.AddMonths(-1), Guid.NewGuid());

        await SetzeAbschlussEntscheidungAsync(db, durchlauf!.Id, studentId: db.StudentProfiles.Single().AppUserId);

        // Keine Zahlung gebucht → volle Schuld (1 Schüler * 12500).
        var (succeeded, _, offeneSchuld) = await service.DurchlaufBeendenAsync(
            durchlauf!.Id, DateTime.UtcNow);

        Assert.True(succeeded);
        Assert.Equal(12500m, offeneSchuld);
        var reload = db.KursNiveauDurchlaeufe.Single();
        Assert.NotNull(reload.EndDatum);
    }

    [Fact]
    public async Task DurchlaufBeendenAsync_DeaktiviertLektionenUndExamsFuerSchueler()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);

        db.Lessons.Add(new Lesson
        {
            Id = Guid.NewGuid(),
            KlasseId = level.Id,
            Title = "Lektion A",
            Summary = "Résumé",
            Content = "Contenu",
            IsPublished = true
        });

        db.Exams.Add(new Exam
        {
            Id = Guid.NewGuid(),
            KlasseId = level.Id,
            Title = "Examen A",
            Description = "Description",
            DurationInMinutes = 30,
            PassingScore = 50,
            IsPublishedToStudents = true,
            IsSharedWithSchool = false
        });
        await db.SaveChangesAsync();

        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow.AddMonths(-1), Guid.NewGuid());
        await SetzeAbschlussEntscheidungAsync(db, durchlauf!.Id, student.Id);

        var (succeeded, _, _) = await service.DurchlaufBeendenAsync(durchlauf.Id, DateTime.UtcNow);

        Assert.True(succeeded);
        Assert.False(db.Lessons.Single().IsPublished);
        Assert.False(db.Exams.Single().IsPublishedToStudents);
    }

    // -------------------- Ratenzahlungen --------------------

    [Fact]
    public async Task ZahlungBuchenAsync_ErzeugtEinnahmeUndReduziertOffeneSchuld()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        var (succeeded, _, zahlung) = await service.ZahlungBuchenAsync(
            durchlauf!.Id, student.Id, 5000m, ZahlungsArt.Rate, DateTime.UtcNow, Guid.NewGuid());

        Assert.True(succeeded);
        Assert.NotNull(zahlung);
        // Automatische Einnahme-Buchung wurde erzeugt.
        Assert.Single(db.FinanzBuchungen);
        Assert.Equal(FinanzRichtung.Einnahme, db.FinanzBuchungen.Single().Richtung);

        var uebersicht = await service.GetDurchlaufUebersichtAsync(durchlauf.Id);
        Assert.NotNull(uebersicht);
        Assert.Equal(5000m, uebersicht!.SummeBezahlt);
        Assert.Equal(7500m, uebersicht.SummeOffen);
    }

    [Fact]
    public async Task GetKlassenFinanzberichtAsync_AggregiertSchulgebuehrUndAboStatus()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.AboTyp = AboTyp.HalloApp;
        enrollment.PriceAtEnrollment = 5000m;
        enrollment.IsPaid = false;
        await db.SaveChangesAsync();
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());
        await service.ZahlungBuchenAsync(
            durchlauf!.Id, student.Id, 5000m, ZahlungsArt.Rate, DateTime.UtcNow, Guid.NewGuid());

        var bericht = await service.GetKlassenFinanzberichtAsync(durchlauf.Id);

        Assert.NotNull(bericht);
        Assert.Equal(1, bericht!.SchuelerAnzahl);
        Assert.Equal(5000m, bericht.SchulgebuehrBezahlt);
        Assert.Equal(7500m, bericht.SchulgebuehrOffen);
        Assert.Equal(0, bericht.HalloPlusAnzahl);
        Assert.Equal(5000m, bericht.HalloAboOffen);
        var berichtsSchueler = Assert.Single(bericht.Schueler);
        Assert.Equal(student.Id, berichtsSchueler.StudentId);
        Assert.Equal(7500m, berichtsSchueler.SchulgebuehrOffen);
        Assert.False(berichtsSchueler.HalloAboBezahlt);
    }

    [Fact]
    public async Task NeuerDurchlaufIgnoriertSchuelerAusGeschlossenemVorjahreszyklus()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, oldStudent) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var newStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Neuer Schüler", UserName = "neu" };
        db.Users.Add(newStudent);
        db.StudentProfiles.Add(new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = newStudent.Id,
            SchoolId = schule.Id,
            CurrentKlasseId = level.Id,
            CurrentLevelCode = level.Code
        });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = newStudent.Id, KlasseId = level.Id });
        await db.SaveChangesAsync();

        var service = new FinanzService(db);
        var (_, _, ersterDurchlauf) = await service.DurchlaufStartenAsync(schule.Id, level.Id, 12500m, DateTime.UtcNow.AddYears(-1), Guid.NewGuid());
        await SetzeAbschlussEntscheidungAsync(db, ersterDurchlauf!.Id, oldStudent.Id, isBestanden: true);
        await SetzeAbschlussEntscheidungAsync(db, ersterDurchlauf.Id, newStudent.Id, isBestanden: true);
        await service.DurchlaufBeendenAsync(ersterDurchlauf.Id, DateTime.UtcNow.AddMonths(-10));

        var oldProfile = db.StudentProfiles.Single(profile => profile.AppUserId == oldStudent.Id);
        oldProfile.CurrentKlasseId = Guid.NewGuid();
        oldProfile.CurrentLevelCode = "A2.1";
        await db.SaveChangesAsync();
        var (_, _, zweiterDurchlauf) = await service.DurchlaufStartenAsync(schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        var uebersicht = await service.GetDurchlaufUebersichtAsync(zweiterDurchlauf!.Id);

        Assert.NotNull(uebersicht);
        var schueler = Assert.Single(uebersicht!.Schueler);
        Assert.Equal(newStudent.Id, schueler.StudentId);
        Assert.Equal(12500m, uebersicht.SummeSoll);
    }

    [Fact]
    public async Task GeschlossenerDurchlaufBehaeltEntscheidungsKohorteNachPromotion()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var zielKlasse = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2", Price = 13000m, SchoolId = schule.Id, IstSystemNiveau = false };
        db.Klassen.Add(zielKlasse);
        await db.SaveChangesAsync();

        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(schule.Id, level.Id, 12500m, DateTime.UtcNow.AddMonths(-3), Guid.NewGuid());
        await SetzeAbschlussEntscheidungAsync(db, durchlauf!.Id, student.Id, isBestanden: true);
        await service.DurchlaufBeendenAsync(durchlauf.Id, DateTime.UtcNow.AddMonths(-1));
        var profile = db.StudentProfiles.Single(item => item.AppUserId == student.Id);
        profile.CurrentKlasseId = zielKlasse.Id;
        profile.CurrentLevelCode = zielKlasse.Code;
        await db.SaveChangesAsync();

        var bericht = await service.GetKlassenFinanzberichtAsync(durchlauf.Id);

        Assert.NotNull(bericht);
        var berichtsSchueler = Assert.Single(bericht!.Schueler);
        Assert.Equal(student.Id, berichtsSchueler.StudentId);
    }

    [Fact]
    public async Task ZahlungBuchenAsync_LehntZahlungNachDurchlaufEndeAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow.AddMonths(-1), Guid.NewGuid());
        await SetzeAbschlussEntscheidungAsync(db, durchlauf!.Id, student.Id);
        await service.DurchlaufBeendenAsync(durchlauf!.Id, DateTime.UtcNow);

        var (succeeded, error, _) = await service.ZahlungBuchenAsync(
            durchlauf.Id, student.Id, 5000m, ZahlungsArt.Rate, DateTime.UtcNow, Guid.NewGuid());

        Assert.False(succeeded);
        Assert.NotEmpty(error);
        var verlustBuchung = Assert.Single(db.FinanzBuchungen);
        Assert.Equal(FinanzRichtung.Ausgabe, verlustBuchung.Richtung);
        Assert.Equal(12500m, verlustBuchung.Betrag);
    }

    private static async Task SetzeAbschlussEntscheidungAsync(TestDbContext db, Guid durchlaufId, Guid studentId, bool isBestanden = false)
    {
        db.KlassenAbschlussEntscheidungen.Add(new KlassenAbschlussEntscheidung
        {
            DurchlaufId = durchlaufId,
            StudentId = studentId,
            IsBestanden = isBestanden,
            EntscheidetAmUtc = DateTime.UtcNow,
            EntscheidetVonUserId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ZahlungBuchenAsync_CashMitAbweichendemGesamtpreis_SchlaegtFehl()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        var (succeeded, _, zahlung) = await service.ZahlungBuchenAsync(
            durchlauf!.Id, student.Id, 5000m, ZahlungsArt.Cash, DateTime.UtcNow, Guid.NewGuid());

        Assert.False(succeeded);
        Assert.Null(zahlung);
        Assert.Empty(db.RatenZahlungen);
    }

    [Fact]
    public async Task ZahlungBuchenAsync_CashMitGesamtpreis_WirdGebucht()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());

        var (succeeded, _, zahlung) = await service.ZahlungBuchenAsync(
            durchlauf!.Id, student.Id, 12500m, ZahlungsArt.Cash, DateTime.UtcNow, Guid.NewGuid());

        Assert.True(succeeded);
        Assert.NotNull(zahlung);
        Assert.Equal(ZahlungsArt.Cash, zahlung!.ZahlungsArt);
        Assert.Equal(12500m, zahlung.Betrag);
        Assert.Equal($"B-{zahlung.FinanzBuchung!.Id:N}".ToUpperInvariant(), zahlung.FinanzBuchung.Buchungsnummer);
    }

    [Fact]
    public async Task GetFinanzAbgleichAsync_ErkenntFehlendeZahlungsbuchung()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());
        var (ok, _, zahlung) = await service.ZahlungBuchenAsync(
            durchlauf!.Id, student.Id, 12500m, ZahlungsArt.Cash, DateTime.UtcNow, Guid.NewGuid());
        Assert.True(ok);

        var korrekt = await service.GetFinanzAbgleichAsync(schule.Id);
        Assert.Equal(12500m, korrekt.SchulgebuehrenZahlungen);
        Assert.Equal(12500m, korrekt.SchulgebuehrenBuchungen);
        Assert.Equal(0, korrekt.SchulgebuehrenAbweichungen);

        db.FinanzBuchungen.Remove(zahlung!.FinanzBuchung!);
        await db.SaveChangesAsync();
        var abweichend = await service.GetFinanzAbgleichAsync(schule.Id);
        Assert.Equal(1, abweichend.SchulgebuehrenAbweichungen);
        Assert.Equal(0m, abweichend.SchulgebuehrenBuchungen);
    }

    [Fact]
    public async Task GetFinanzAbgleichAsync_TrenntPlattformRechnungVonBestaetigterZahlung()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        SeedNiveauMitSchueler(db, schule);
        var enrollment = db.LevelEnrollments.Single();
        enrollment.IsPaid = true;
        enrollment.PaidAtUtc = DateTime.UtcNow;
        enrollment.PriceAtEnrollment = 20_000m;
        enrollment.SchoolLeaderCommissionAtEnrollment = 800m;
        enrollment.TeacherCommissionAtEnrollment = 1_600m;
        var userId = Guid.NewGuid();
        db.Users.Add(new AppUser { Id = userId, UserName = "buchhalter" });
        var rechnung = new Abrechnungsperiode { SchulID = schule.Id, Umsatz = 17_600m, Status = AbrechnungStatus.Erstellt };
        db.Abrechnungsperioden.Add(rechnung);
        enrollment.AbgerechnetInPeriodeId = rechnung.Id;
        await db.SaveChangesAsync();
        var service = new FinanzService(db);
        var gebucht = await service.HalloAppAboBuchungenErstellenAsync(enrollment.Id, userId);
        Assert.True(gebucht.Succeeded);

        var offen = await service.GetFinanzAbgleichAsync(schule.Id);
        Assert.Equal(17_600m, offen.PlattformRechnungen);
        Assert.Equal(17_600m, offen.PlattformBuchungenZuRechnungen);
        Assert.Equal(0m, offen.BestaetigtePlattformZahlungen);
        Assert.Equal(0, offen.PlattformRechnungenMitAbweichung);

        rechnung.Status = AbrechnungStatus.Bestaetigt;
        await db.SaveChangesAsync();
        var bestaetigt = await service.GetFinanzAbgleichAsync(schule.Id);
        Assert.Equal(17_600m, bestaetigt.BestaetigtePlattformZahlungen);
        Assert.Equal(17_600m, bestaetigt.PlattformBuchungenZuRechnungen);

        rechnung.Umsatz = 17_500m;
        await db.SaveChangesAsync();
        var abweichend = await service.GetFinanzAbgleichAsync(schule.Id);
        Assert.Equal(1, abweichend.PlattformRechnungenMitAbweichung);
    }

    [Fact]
    public async Task ZahlungBuchenAsync_CashMitOffenemRestbetrag_WirdGebucht()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, student) = SeedNiveauMitSchueler(db, schule, preis: 12500m);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());
        await service.ZahlungBuchenAsync(
            durchlauf!.Id, student.Id, 5000m, ZahlungsArt.Rate, DateTime.UtcNow, Guid.NewGuid());

        var (succeeded, _, zahlung) = await service.ZahlungBuchenAsync(
            durchlauf.Id, student.Id, 7500m, ZahlungsArt.Cash, DateTime.UtcNow, Guid.NewGuid());

        Assert.True(succeeded);
        Assert.NotNull(zahlung);
        Assert.Equal(7500m, zahlung!.Betrag);
    }

    [Fact]
    public async Task ZahlungBuchenAsync_NichtEingeschriebenerSchueler_SchlaegtFehl()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var (level, _) = SeedNiveauMitSchueler(db, schule);
        var service = new FinanzService(db);
        var (_, _, durchlauf) = await service.DurchlaufStartenAsync(
            schule.Id, level.Id, 12500m, DateTime.UtcNow, Guid.NewGuid());
        var fremderStudent = new AppUser { Id = Guid.NewGuid(), FullName = "Nicht eingeschrieben", UserName = "fremd" };
        db.Users.Add(fremderStudent);
        await db.SaveChangesAsync();

        var (succeeded, _, zahlung) = await service.ZahlungBuchenAsync(
            durchlauf!.Id, fremderStudent.Id, 5000m, ZahlungsArt.Rate, DateTime.UtcNow, Guid.NewGuid());

        Assert.False(succeeded);
        Assert.Null(zahlung);
    }

    // -------------------- Löschregel für Abo-Buchungen --------------------

    [Fact]
    public async Task BuchungLoeschenAsync_AboBuchung_NurVomSuperAdminStornierbar()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = SeedSchule(db);
        var ersteller = Guid.NewGuid();

        // Automatische Abo-Buchung mit System-Kategorie simulieren.
        var kategorie = new FinanzKategorie
        {
            SchulID = null,
            Bezeichnung = "HalloApp-Abo-Zahlung",
            Richtung = FinanzRichtung.Einnahme,
            SystemCode = FinanzSystemCode.HalloAppAboZahlung,
            IstSystemTyp = true,
            IstAktiv = true
        };
        db.FinanzKategorien.Add(kategorie);
        var buchung = new FinanzBuchung
        {
            SchulID = schule.Id,
            KategorieID = kategorie.Id,
            Kategorie = kategorie,
            Richtung = FinanzRichtung.Einnahme,
            Betrag = 12500m,
            Wirkungsdatum = DateTime.UtcNow,
            ErfasstVonUserID = ersteller
        };
        db.FinanzBuchungen.Add(buchung);
        db.SaveChanges();
        var service = new FinanzService(db);

        // Ersteller (kein SuperAdmin) darf NICHT stornieren.
        var alsErsteller = await service.BuchungLoeschenAsync(buchung.Id, ersteller, istSuperAdmin: false);
        Assert.False(alsErsteller.Succeeded);
        Assert.NotEmpty(alsErsteller.ErrorMessage);

        // SuperAdmin darf stornieren.
        var alsSuperAdmin = await service.BuchungLoeschenAsync(buchung.Id, Guid.NewGuid(), istSuperAdmin: true);
        Assert.True(alsSuperAdmin.Succeeded);
        Assert.Empty(db.FinanzBuchungen);
    }

    // -------------------- Übersicht: Filter & Sortierung --------------------

    private static async Task<(FinanzService Service, School Schule)> SeedBuchungenFuerUebersichtAsync(TestDbContext db)
    {
        var schule = SeedSchule(db);
        var user = new AppUser { Id = Guid.NewGuid(), FullName = "Direktor", UserName = "direktor" };
        db.Users.Add(user);
        db.SaveChanges();

        var service = new FinanzService(db, new FakeFinanzBelegStorage());
        var (_, _, einnahmeKat) = await service.KategorieAnlegenAsync(schule.Id, "Verkauf", FinanzRichtung.Einnahme);
        var (_, _, ausgabeKat) = await service.KategorieAnlegenAsync(schule.Id, "Miete", FinanzRichtung.Ausgabe);

        var b1 = await service.BuchungErfassenAsync(schule.Id, einnahmeKat!.Id, 10000m, new DateTime(2026, 1, 10), user.Id, belegnummer: "REF-06", dateiname: "facture.pdf", belegInhalt: TestPdf);
        Assert.True(b1.Succeeded, b1.ErrorMessage);
        await service.BuchungErfassenAsync(schule.Id, einnahmeKat.Id, 2000m, new DateTime(2026, 3, 5), user.Id, belegnummer: "REF-07", dateiname: "facture.pdf", belegInhalt: TestPdf);
        await service.BuchungErfassenAsync(schule.Id, ausgabeKat!.Id, 3000m, new DateTime(2026, 2, 1), user.Id, belegnummer: "REF-08", dateiname: "facture.pdf", belegInhalt: TestPdf);

        return (service, schule);
    }

    [Fact]
    public async Task GetUebersichtAsync_BerechnetSummenUndSaldo()
    {
        using var db = TestDbContext.CreateInMemory();
        var (service, schule) = await SeedBuchungenFuerUebersichtAsync(db);

        var uebersicht = await service.GetUebersichtAsync(new FinanzFilter { SchulID = schule.Id });

        Assert.Equal(3, uebersicht.Zeilen.Count);
        Assert.Equal(12000m, uebersicht.SummeEinnahmen);
        Assert.Equal(3000m, uebersicht.SummeAusgaben);
        Assert.Equal(9000m, uebersicht.Saldo);
    }

    [Fact]
    public async Task GetUebersichtAsync_FiltertNachRichtung()
    {
        using var db = TestDbContext.CreateInMemory();
        var (service, schule) = await SeedBuchungenFuerUebersichtAsync(db);

        var nurAusgaben = await service.GetUebersichtAsync(new FinanzFilter
        {
            SchulID = schule.Id,
            Richtung = FinanzRichtung.Ausgabe
        });

        Assert.Single(nurAusgaben.Zeilen);
        Assert.Equal(3000m, nurAusgaben.SummeAusgaben);
        Assert.Equal(0m, nurAusgaben.SummeEinnahmen);
    }

    [Fact]
    public async Task GetUebersichtAsync_FiltertNachDatumsbereich()
    {
        using var db = TestDbContext.CreateInMemory();
        var (service, schule) = await SeedBuchungenFuerUebersichtAsync(db);

        var februarPlus = await service.GetUebersichtAsync(new FinanzFilter
        {
            SchulID = schule.Id,
            Von = new DateTime(2026, 2, 1)
        });

        Assert.Equal(2, februarPlus.Zeilen.Count);
        Assert.DoesNotContain(februarPlus.Zeilen, x => x.Wirkungsdatum == new DateTime(2026, 1, 10));
    }

    [Fact]
    public async Task GetUebersichtAsync_SortiertNachBetragAbsteigend()
    {
        using var db = TestDbContext.CreateInMemory();
        var (service, schule) = await SeedBuchungenFuerUebersichtAsync(db);

        var uebersicht = await service.GetUebersichtAsync(new FinanzFilter
        {
            SchulID = schule.Id,
            Sortierung = FinanzSortierung.BetragAbsteigend
        });

        var betraege = uebersicht.Zeilen.Select(x => x.Betrag).ToList();
        Assert.Equal(new[] { 10000m, 3000m, 2000m }, betraege);
    }
}
