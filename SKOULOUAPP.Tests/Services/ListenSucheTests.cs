using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Email;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Models.Student;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Components.Pages;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für die serverseitige Suche und das seitenweise Laden wachsender Listen
/// („Afficher plus“: Skip/Take mit anzahl + 1) sowie für die zugehörigen Fehlerkorrekturen.
/// </summary>
public class ListenSucheTests
{
    // ---------- Plattform-Nutzer (Admin) ----------

    private static AppUser NutzerMitRollen(DbContext db, string name, params Role[] rollen)
    {
        var user = new AppUser { Id = Guid.NewGuid(), FullName = name, UserName = name.ToLowerInvariant().Replace(' ', '.'), Email = $"{name.ToLowerInvariant().Replace(' ', '.')}@test.local" };
        foreach (var rolle in rollen)
        {
            user.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = rolle.Id, Role = rolle, AppUser = user });
        }

        db.Set<AppUser>().Add(user);
        return user;
    }

    private static Dictionary<string, Role> RollenAnlegen(DbContext db)
    {
        var rollen = new[] { Rollen.SuperAdmin, Rollen.ProduktOwner, Rollen.SchulLeiter, Rollen.Lehrer, Rollen.Schueler }
            .ToDictionary(name => name, name => new Role { Id = Guid.NewGuid(), Name = name });
        db.Set<Role>().AddRange(rollen.Values);
        return rollen;
    }

    [Fact]
    public async Task SuchePlatformUsersAsync_LaedtSeitenweiseSortiertNachHauptrolleUndName()
    {
        using var db = TestDbContext.CreateInMemory();
        var rollen = RollenAnlegen(db);
        var admin = NutzerMitRollen(db, "Admin", rollen[Rollen.SuperAdmin]);
        // Direktor hat zusätzlich die Rolle Lehrer: Hauptrolle ist Directeur.
        NutzerMitRollen(db, "Zoe Direktor", rollen[Rollen.Lehrer], rollen[Rollen.SchulLeiter]);
        NutzerMitRollen(db, "Carl Lehrer", rollen[Rollen.Lehrer]);
        NutzerMitRollen(db, "Anna Lehrer", rollen[Rollen.Lehrer]);
        NutzerMitRollen(db, "Bert Lehrer", rollen[Rollen.Lehrer]);
        NutzerMitRollen(db, "Emil Schueler", rollen[Rollen.Schueler]);
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create(db.Users.ToList());
        manager.SeedRole(admin, Rollen.SuperAdmin);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        var (seite1, mehr1) = await service.SuchePlatformUsersAsync(admin.Id, null, null, null, 0, 3);
        var (seite2, mehr2) = await service.SuchePlatformUsersAsync(admin.Id, null, null, null, 3, 3);

        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal(["Admin", "Zoe Direktor", "Anna Lehrer"], seite1.Select(u => u.FullName));
        Assert.Equal(["Bert Lehrer", "Carl Lehrer", "Emil Schueler"], seite2.Select(u => u.FullName));
        Assert.Equal(Rollen.SchulLeiter, seite1[1].RoleName);
    }

    [Fact]
    public async Task SuchePlatformUsersAsync_FiltertNachSucheRolleUndSchuleUndZaehltJeRolle()
    {
        using var db = TestDbContext.CreateInMemory();
        var rollen = RollenAnlegen(db);
        var schuleA = new School { Id = Guid.NewGuid(), Name = "École A" };
        var schuleB = new School { Id = Guid.NewGuid(), Name = "École B" };
        db.Schools.AddRange(schuleA, schuleB);
        var admin = NutzerMitRollen(db, "Admin", rollen[Rollen.SuperAdmin]);
        var anna = NutzerMitRollen(db, "Anna Lehrer", rollen[Rollen.Lehrer]);
        var bert = NutzerMitRollen(db, "Bert Lehrer", rollen[Rollen.Lehrer]);
        var emil = NutzerMitRollen(db, "Emil Schueler", rollen[Rollen.Schueler]);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = anna.Id, SchoolId = schuleA.Id },
            new UserSchool { AppUserId = bert.Id, SchoolId = schuleB.Id },
            new UserSchool { AppUserId = emil.Id, SchoolId = schuleA.Id });
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create(db.Users.ToList());
        manager.SeedRole(admin, Rollen.SuperAdmin);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        var (nachName, _) = await service.SuchePlatformUsersAsync(admin.Id, "Bert", null, null, 0);
        var (nachRolle, _) = await service.SuchePlatformUsersAsync(admin.Id, null, Rollen.Lehrer, null, 0);
        var (nachSchule, _) = await service.SuchePlatformUsersAsync(admin.Id, null, null, schuleA.Id, 0);
        var (nachRollenText, _) = await service.SuchePlatformUsersAsync(admin.Id, "Élève", null, null, 0);
        var anzahl = await service.ZaehlePlatformUsersNachRolleAsync(admin.Id, null, schuleA.Id);

        Assert.Equal(bert.Id, Assert.Single(nachName).UserId);
        Assert.Equal([anna.Id, bert.Id], nachRolle.Select(u => u.UserId));
        Assert.Equal([anna.Id, emil.Id], nachSchule.Select(u => u.UserId));
        Assert.Equal(["École A"], nachSchule[0].Schools);
        Assert.Equal(emil.Id, Assert.Single(nachRollenText).UserId);
        Assert.Equal(1, anzahl[Rollen.Lehrer]);
        Assert.Equal(1, anzahl[Rollen.Schueler]);
        Assert.Equal(2, anzahl[string.Empty]);
    }

    [Fact]
    public async Task SuchePlatformUsersAsync_ResponsableSiehtNurKontenSeinerSchulenOhneAdmins()
    {
        using var db = TestDbContext.CreateInMemory();
        var rollen = RollenAnlegen(db);
        var eigene = new School { Id = Guid.NewGuid(), Name = "Eigene" };
        var fremde = new School { Id = Guid.NewGuid(), Name = "Fremde" };
        db.Schools.AddRange(eigene, fremde);
        var owner = NutzerMitRollen(db, "Owner", rollen[Rollen.ProduktOwner]);
        var lehrer = NutzerMitRollen(db, "Eigener Lehrer", rollen[Rollen.Lehrer]);
        var fremd = NutzerMitRollen(db, "Fremder Lehrer", rollen[Rollen.Lehrer]);
        var admin = NutzerMitRollen(db, "Admin", rollen[Rollen.SuperAdmin]);
        db.UserSchools.AddRange(
            new UserSchool { AppUserId = owner.Id, SchoolId = eigene.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = eigene.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = fremde.Id },
            new UserSchool { AppUserId = fremd.Id, SchoolId = fremde.Id },
            new UserSchool { AppUserId = admin.Id, SchoolId = eigene.Id });
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create(db.Users.ToList());
        manager.SeedRole(owner, Rollen.ProduktOwner);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        var (eintraege, mehr) = await service.SuchePlatformUsersAsync(owner.Id, null, null, null, 0);
        var anzahl = await service.ZaehlePlatformUsersNachRolleAsync(owner.Id, null, null);

        var sichtbar = Assert.Single(eintraege);
        Assert.False(mehr);
        Assert.Equal(lehrer.Id, sichtbar.UserId);
        Assert.Equal(["Eigene"], sichtbar.Schools);
        Assert.Equal(1, anzahl[string.Empty]);
    }

    // ---------- Medienbibliothek ----------

    [Fact]
    public async Task SucheMediaLibraryAsync_SuchtSeitenweiseUndResponsableSiehtNurEigeneSchulen()
    {
        using var db = TestDbContext.CreateInMemory();
        var rollen = RollenAnlegen(db);
        var eigene = new School { Id = Guid.NewGuid(), Name = "Alpha" };
        var fremde = new School { Id = Guid.NewGuid(), Name = "Beta" };
        db.Schools.AddRange(eigene, fremde);
        var admin = NutzerMitRollen(db, "Admin", rollen[Rollen.SuperAdmin]);
        var owner = NutzerMitRollen(db, "Owner", rollen[Rollen.ProduktOwner]);
        db.UserSchools.Add(new UserSchool { AppUserId = owner.Id, SchoolId = eigene.Id });
        for (var i = 1; i <= 3; i++)
        {
            db.BlobMediaAssets.Add(new BlobMediaAsset { Id = Guid.NewGuid(), SchoolId = eigene.Id, OwnerId = admin.Id, DisplayName = $"Carte {i}", OriginalFileName = $"carte{i}.png", ContentType = "image/png", Length = 100 });
        }
        db.BlobMediaAssets.Add(new BlobMediaAsset { Id = Guid.NewGuid(), SchoolId = fremde.Id, OwnerId = admin.Id, DisplayName = "Photo", OriginalFileName = "photo.jpg", ContentType = "image/jpeg", Length = 1000 });
        await db.SaveChangesAsync();
        var manager = TestUserManager.Create(db.Users.ToList());
        manager.SeedRole(admin, Rollen.SuperAdmin);
        manager.SeedRole(owner, Rollen.ProduktOwner);
        var service = new DashboardService(db, manager, new FakeMediaStorageService());

        var (seite1, mehr1) = await service.SucheMediaLibraryAsync(admin.Id, null, null, 0, 2);
        var (seite2, mehr2) = await service.SucheMediaLibraryAsync(admin.Id, null, null, 2, 2);
        var (gesucht, _) = await service.SucheMediaLibraryAsync(admin.Id, null, "Photo", 0);
        var (ownerSicht, _) = await service.SucheMediaLibraryAsync(owner.Id, null, null, 0);
        var gesamt = await service.ZaehleMediaLibraryAsync(admin.Id, null, null);
        var gefiltert = await service.ZaehleMediaLibraryAsync(admin.Id, eigene.Id, null);
        var schulen = await service.GetMediaLibrarySchulenAsync(owner.Id);

        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal(["Carte 1", "Carte 2"], seite1.Select(m => m.DisplayName));
        Assert.Equal(["Carte 3", "Photo"], seite2.Select(m => m.DisplayName));
        Assert.Equal("Beta", Assert.Single(gesucht).SchoolName);
        Assert.Equal(3, ownerSicht.Count);
        Assert.All(ownerSicht, media => Assert.Equal(eigene.Id, media.SchoolId));
        Assert.Equal((4, 1300L), gesamt);
        Assert.Equal((3, 300L), gefiltert);
        Assert.Equal(new MediaLibrarySchuleErwartet(eigene.Id, 3, 300), schulen.Select(s => new MediaLibrarySchuleErwartet(s.SchoolId, s.Anzahl, s.Bytes)).Single());
    }

    private sealed record MediaLibrarySchuleErwartet(Guid SchoolId, int Anzahl, long Bytes);

    // ---------- Bulletins (Fehlerkorrektur: Filter Année scolaire / Période / Classe) ----------

    [Fact]
    public async Task BulletinSucheListeAsync_BeruecksichtigtJahrPeriodeKlasseUndSuche()
    {
        var factory = new TestApplicationDbContextFactory();
        var schule = Guid.NewGuid();
        var direktor = Guid.NewGuid();
        var jahr1 = Guid.NewGuid(); var jahr2 = Guid.NewGuid();
        var klasseA = Guid.NewGuid(); var klasseB = Guid.NewGuid();
        var periode1 = Guid.NewGuid(); var periode2 = Guid.NewGuid(); var periodeAltesJahr = Guid.NewGuid();
        var ama = Guid.NewGuid(); var kofi = Guid.NewGuid();
        Guid zuordnungAma, zuordnungKofi, zuordnungAmaAlt;
        await using (var db = factory.CreateDbContext())
        {
            db.Schools.Add(new School { Id = schule, Name = "École" });
            var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            db.Roles.Add(rolle);
            db.Users.Add(new AppUser { Id = direktor, FullName = "Direction" });
            db.UserRoles.Add(new AppUserRole { UserId = direktor, RoleId = rolle.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = direktor, SchoolId = schule });
            db.Users.Add(new AppUser { Id = ama, FullName = "Ama Mensah" });
            db.Users.Add(new AppUser { Id = kofi, FullName = "Kofi Agbo" });
            db.Klassen.AddRange(new Klasse { Id = klasseA, SchoolId = schule, Code = "6eA" }, new Klasse { Id = klasseB, SchoolId = schule, Code = "6eB" });
            db.Add(new Schuljahr { Id = jahr1, SchulID = schule, Bezeichnung = "2025–2026", Beginn = new(2025, 9, 1), Ende = new(2026, 7, 31) });
            db.Add(new Schuljahr { Id = jahr2, SchulID = schule, Bezeichnung = "2026–2027", Beginn = new(2026, 9, 1), Ende = new(2027, 7, 31) });
            db.Add(new BewertungsPeriode { Id = periodeAltesJahr, SchulID = schule, SchuljahrID = jahr1, Titel = "3e trimestre", Art = BewertungsArt.Trimester, Reihenfolge = 3 });
            db.Add(new BewertungsPeriode { Id = periode1, SchulID = schule, SchuljahrID = jahr2, Titel = "1er trimestre", Art = BewertungsArt.Trimester, Reihenfolge = 1 });
            db.Add(new BewertungsPeriode { Id = periode2, SchulID = schule, SchuljahrID = jahr2, Titel = "2e trimestre", Art = BewertungsArt.Trimester, Reihenfolge = 2 });
            var zAma = new SchuelerJahresZuordnung { Id = Guid.NewGuid(), SchulID = schule, SchuljahrID = jahr2, KlasseID = klasseA, SchuelerID = ama };
            var zKofi = new SchuelerJahresZuordnung { Id = Guid.NewGuid(), SchulID = schule, SchuljahrID = jahr2, KlasseID = klasseB, SchuelerID = kofi };
            var zAmaAlt = new SchuelerJahresZuordnung { Id = Guid.NewGuid(), SchulID = schule, SchuljahrID = jahr1, KlasseID = klasseA, SchuelerID = ama };
            db.AddRange(zAma, zKofi, zAmaAlt);
            (zuordnungAma, zuordnungKofi, zuordnungAmaAlt) = (zAma.Id, zKofi.Id, zAmaAlt.Id);
            var zeit = DateTime.UtcNow;
            foreach (var (zuordnung, periodeId, minuten) in new[] { (zuordnungAmaAlt, periodeAltesJahr, 1), (zuordnungAma, periode1, 2), (zuordnungKofi, periode1, 3), (zuordnungAma, periode2, 4) })
            {
                db.Add(new BulletinAusgabe
                {
                    Id = Guid.NewGuid(), SchulID = schule, PeriodeID = periodeId, JahresZuordnungID = zuordnung, Version = 1,
                    IstVeroeffentlicht = true, ErstelltVonID = direktor, SnapshotJson = "{}", ErstelltAmUtc = zeit.AddMinutes(minuten)
                });
            }
            await db.SaveChangesAsync();
        }

        var service = new BulletinService(factory, new BewertungsService(factory));

        var jahr = await service.SucheListeAsync(direktor, schule, new BulletinListenFilter(SchuljahrID: jahr2), 0);
        var periode = await service.SucheListeAsync(direktor, schule, new BulletinListenFilter(jahr2, periode1), 0);
        var klasse = await service.SucheListeAsync(direktor, schule, new BulletinListenFilter(jahr2, periode1, klasseB), 0);
        var suche = await service.SucheListeAsync(direktor, schule, new BulletinListenFilter(Suche: "Ama"), 0);
        var (seite1, mehr1) = await service.SucheListeAsync(direktor, schule, new BulletinListenFilter(), 0, 3);
        var (seite2, mehr2) = await service.SucheListeAsync(direktor, schule, new BulletinListenFilter(), 3, 3);

        Assert.Equal(3, jahr.Eintraege.Count);
        Assert.Equal(["Kofi Agbo", "Ama Mensah"], periode.Eintraege.Select(x => x.Schueler));
        Assert.Equal("Kofi Agbo", Assert.Single(klasse.Eintraege).Schueler);
        Assert.Equal(3, suche.Eintraege.Count);
        Assert.All(suche.Eintraege, x => Assert.Equal("Ama Mensah", x.Schueler));
        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal("2e trimestre", seite1[0].Titel);
        Assert.Equal("3e trimestre", Assert.Single(seite2).Titel);
    }

    // ---------- Messagerie ----------

    [Fact]
    public async Task MailboxSucheAsync_TrenntOrdnerSuchtUndLaedtInhaltNurBeiZugriff()
    {
        var factory = new TestApplicationDbContextFactory();
        var adminId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var schoolId = Guid.NewGuid();
        Guid fremdeMail;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var adminRole = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin };
            var ownerRole = new Role { Id = Guid.NewGuid(), Name = Rollen.ProduktOwner };
            var admin = new AppUser { Id = adminId, UserName = "admin" };
            admin.UserRoles.Add(new AppUserRole { UserId = adminId, RoleId = adminRole.Id, Role = adminRole, AppUser = admin });
            var owner = new AppUser { Id = ownerId, UserName = "owner" };
            owner.UserRoles.Add(new AppUserRole { UserId = ownerId, RoleId = ownerRole.Id, Role = ownerRole, AppUser = owner });
            db.Roles.AddRange(adminRole, ownerRole);
            db.Users.AddRange(admin, owner);
            db.Schools.Add(new School { Id = schoolId, Name = "École" });
            db.UserSchools.Add(new UserSchool { AppUserId = ownerId, SchoolId = schoolId });
            var zeit = DateTime.UtcNow;
            for (var i = 1; i <= 3; i++)
            {
                db.MailNachrichten.Add(new MailNachricht { Id = Guid.NewGuid(), IstEingehend = true, Absender = $"parent{i}@test.local", Empfaenger = "contact@test.local", Betreff = $"Question {i}", Inhalt = $"<p>Texte {i}</p>", ErstelltAmUtc = zeit.AddMinutes(i), SchulID = schoolId });
            }
            var fremd = new MailNachricht { Id = Guid.NewGuid(), IstEingehend = true, Absender = "autre@test.local", Empfaenger = "contact@test.local", Betreff = "Hors école", Inhalt = "<p>Confidentiel</p>", ErstelltAmUtc = zeit };
            db.MailNachrichten.Add(fremd);
            db.MailNachrichten.Add(new MailNachricht { Id = Guid.NewGuid(), IstEingehend = false, Absender = "contact@test.local", Empfaenger = "parent1@test.local", Betreff = "Réponse", Inhalt = "<p>Merci</p>", ErstelltAmUtc = zeit });
            await db.SaveChangesAsync();
            fremdeMail = fremd.Id;
        }

        var service = new MailboxService(factory, new FakeEmailService());

        var (seite1, mehr1) = await service.SucheAsync(adminId, null, true, null, 0, 2);
        var (seite2, mehr2) = await service.SucheAsync(adminId, null, true, null, 2, 2);
        var (gesendet, _) = await service.SucheAsync(adminId, null, false, null, 0);
        var (imText, _) = await service.SucheAsync(adminId, null, true, "Texte 2", 0);
        var (ownerSicht, _) = await service.SucheAsync(ownerId, null, true, null, 0);

        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal(["Question 3", "Question 2"], seite1.Select(m => m.Betreff));
        Assert.Equal(["Question 1", "Hors école"], seite2.Select(m => m.Betreff));
        Assert.Equal("Réponse", Assert.Single(gesendet).Betreff);
        Assert.Equal("Question 2", Assert.Single(imText).Betreff);
        Assert.Equal(3, ownerSicht.Count);
        Assert.Equal("<p>Confidentiel</p>", await service.InhaltAsync(adminId, fremdeMail));
        Assert.Null(await service.InhaltAsync(ownerId, fremdeMail));
    }

    // ---------- Fehlerjournal ----------

    [Fact]
    public async Task ExceptionLogSuche_FiltertNachZeitraumSucheUndLaedtSeitenweise()
    {
        var factory = new TestApplicationDbContextFactory();
        await using var db = await factory.CreateDbContextAsync();
        var heute = DateTime.UtcNow.Date.AddHours(12);
        for (var i = 0; i < 5; i++)
        {
            db.ExceptionLogs.Add(new ExceptionLog { Message = $"Timeout {i}", ExceptionType = "SqlException", OccurredAtUtc = heute.AddMinutes(-i) });
        }
        db.ExceptionLogs.Add(new ExceptionLog { Message = "NullReference", RequestPath = "/admin", OccurredAtUtc = heute.AddMinutes(-10), IsHandled = true });
        db.ExceptionLogs.Add(new ExceptionLog { Message = "Ancienne erreur", OccurredAtUtc = heute.AddDays(-30) });
        await db.SaveChangesAsync();

        var (seite1, mehr1) = await ExceptionLogSuche.SucheAsync(db, new ExceptionLogFilter(heute.AddDays(-7), heute), 0, 4);
        var (seite2, mehr2) = await ExceptionLogSuche.SucheAsync(db, new ExceptionLogFilter(heute.AddDays(-7), heute), 4, 4);
        var (suche, _) = await ExceptionLogSuche.SucheAsync(db, new ExceptionLogFilter(Suche: "/admin"), 0);
        var (offen, _) = await ExceptionLogSuche.SucheAsync(db, new ExceptionLogFilter(heute.AddDays(-7), heute, NurOffene: true), 0);
        var (alle, _) = await ExceptionLogSuche.SucheAsync(db, new ExceptionLogFilter(), 0);

        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal(["Timeout 0", "Timeout 1", "Timeout 2", "Timeout 3"], seite1.Select(x => x.Message));
        Assert.Equal(["Timeout 4", "NullReference"], seite2.Select(x => x.Message));
        Assert.Equal("NullReference", Assert.Single(suche).Message);
        Assert.Equal(5, offen.Count);
        Assert.Equal(7, alle.Count);
    }

    // ---------- Zyklen (abrechnungspflichtige Teilnehmer) ----------

    [Fact]
    public async Task SucheZyklenAsync_NurAdminSuchtUndFiltertLaufende()
    {
        // ApplicationDbContext: dort ist die Beziehung Zyklus → Schule (SchulID) konfiguriert.
        await using var db = new TestApplicationDbContextFactory().CreateDbContext();
        var rollen = RollenAnlegen(db);
        var admin = NutzerMitRollen(db, "Admin", rollen[Rollen.SuperAdmin]);
        var direktor = NutzerMitRollen(db, "Direktor", rollen[Rollen.SchulLeiter]);
        var alpha = new School { Id = Guid.NewGuid(), Name = "Alpha" };
        var beta = new School { Id = Guid.NewGuid(), Name = "Beta" };
        db.Schools.AddRange(alpha, beta);
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = alpha.Id, Code = "CM1", Title = "CM1" };
        var klasseBeta = new Klasse { Id = Guid.NewGuid(), SchoolId = beta.Id, Code = "CE2", Title = "CE2" };
        db.Klassen.AddRange(klasse, klasseBeta);
        db.KursNiveauDurchlaeufe.AddRange(
            new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = alpha.Id, KlasseId = klasse.Id, StartDatum = DateTime.UtcNow.AddYears(-1), EndDatum = DateTime.UtcNow.AddMonths(-2), PlattformPreisProSchueler = 1000m },
            new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = alpha.Id, KlasseId = klasse.Id, StartDatum = DateTime.UtcNow.AddMonths(-1), PlattformPreisProSchueler = 1000m },
            new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = beta.Id, KlasseId = klasseBeta.Id, StartDatum = DateTime.UtcNow, PlattformPreisProSchueler = 1000m },
            new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = beta.Id, KlasseId = klasseBeta.Id, StartDatum = DateTime.UtcNow.AddYears(-2) });
        await db.SaveChangesAsync();
        var service = new ZyklusTeilnahmeService(db);

        var (seite1, mehr1) = await service.SucheZyklenAsync(admin.Id, null, false, 0, 2);
        var (seite2, mehr2) = await service.SucheZyklenAsync(admin.Id, null, false, 2, 2);
        var (laufende, _) = await service.SucheZyklenAsync(admin.Id, null, true, 0);
        var (beta2, _) = await service.SucheZyklenAsync(admin.Id, "Beta", false, 0);
        var (keinAdmin, _) = await service.SucheZyklenAsync(direktor.Id, null, false, 0);

        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal(["Alpha · CM1 · En cours", "Alpha · CM1 · Clôturé"], seite1.Select(z => z.Label));
        Assert.Equal("Beta", Assert.Single(seite2).SchulName);
        Assert.Equal(2, laufende.Count);
        Assert.All(laufende, z => Assert.False(z.Geschlossen));
        Assert.Equal("CE2", Assert.Single(beta2).KlasseCode);
        Assert.Empty(keinAdmin);
    }

    // ---------- Ankündigungen ----------

    [Fact]
    public async Task AnkuendigungSucheAsync_SuchtImTitelLaedtSeitenweiseOhneInhalt()
    {
        using var db = TestDbContext.CreateInMemory();
        var jetzt = DateTime.UtcNow;
        for (var i = 1; i <= 3; i++)
        {
            db.Ankuendigungen.Add(new Ankuendigung { Titel = $"Info {i}", InhaltHtml = "<p>Long texte</p>", ErstelltAmUtc = jetzt.AddMinutes(i), AblaufAmUtc = jetzt.AddDays(i) });
        }
        db.Ankuendigungen.Add(new Ankuendigung { Titel = "Maintenance", InhaltHtml = "<p>Arrêt</p>", ErstelltAmUtc = jetzt, AblaufAmUtc = jetzt.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = new AnkuendigungService(db);

        var (seite1, mehr1) = await service.SucheAsync(null, 0, 2);
        var (seite2, mehr2) = await service.SucheAsync(null, 2, 2);
        var (gesucht, _) = await service.SucheAsync("Maint", 0);

        Assert.True(mehr1);
        Assert.False(mehr2);
        Assert.Equal(["Info 3", "Info 2"], seite1.Select(a => a.Titel));
        Assert.Equal(["Info 1", "Maintenance"], seite2.Select(a => a.Titel));
        Assert.Equal("Maintenance", Assert.Single(gesucht).Titel);
        Assert.All(seite1, a => Assert.Equal(string.Empty, a.InhaltHtml));
    }

    // ---------- Schülerlektionen (Fehlerkorrektur: erst filtern, dann blättern) ----------

    [Fact]
    public void StudentLessons_FiltereLektionen_FiltertVorDemBlaettern()
    {
        // 12 offene Lektionen und 3 abgeschlossene am Ende der Reihenfolge: Mit Filter „Terminé“
        // müssen alle 3 auf der ersten Seite (10 Einträge) erscheinen – früher war die erste Seite leer.
        var lessons = Enumerable.Range(1, 15).Select(i => new LessonListItem
        {
            LessonId = Guid.NewGuid(), Title = $"Leçon {i}", CourseCode = "CM1", Order = i, IsCompleted = i > 12, FachName = i % 2 == 0 ? "Maths" : "Français"
        }).Reverse().ToList();

        var terminees = StudentLessons.FiltereLektionen(lessons, null, null, null, "completed");
        var ersteSeite = terminees.Take(10).ToList();
        var maths = StudentLessons.FiltereLektionen(lessons, "Leçon 1", "Maths", null, "all");

        Assert.Equal(["Leçon 13", "Leçon 14", "Leçon 15"], ersteSeite.Select(l => l.Title));
        Assert.Equal(["Leçon 10", "Leçon 12", "Leçon 14"], maths.Select(l => l.Title));
    }

    // ---------- Finanzen ----------

    [Fact]
    public async Task GetDurchlaeufeAsync_ErmitteltTeilnehmerGebuendeltWieBisher()
    {
        using var db = TestDbContext.CreateInMemory();
        var schule = new School { Id = Guid.NewGuid(), Name = "École", IsActive = true };
        db.Schools.Add(schule);
        var klasseAktiv = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM1" };
        var klasseEntschieden = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CM2" };
        var klasseAlt = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "CE2" };
        db.Klassen.AddRange(klasseAktiv, klasseEntschieden, klasseAlt);
        var ama = new AppUser { Id = Guid.NewGuid(), FullName = "Ama" };
        var kofi = new AppUser { Id = Guid.NewGuid(), FullName = "Kofi" };
        var yao = new AppUser { Id = Guid.NewGuid(), FullName = "Yao" };
        db.Users.AddRange(ama, kofi, yao);
        db.StudentProfiles.AddRange(
            new StudentProfile { Id = Guid.NewGuid(), AppUserId = ama.Id, SchoolId = schule.Id, CurrentKlasseId = klasseAktiv.Id },
            new StudentProfile { Id = Guid.NewGuid(), AppUserId = kofi.Id, SchoolId = schule.Id },
            new StudentProfile { Id = Guid.NewGuid(), AppUserId = yao.Id, SchoolId = schule.Id });
        var aktiv = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = schule.Id, KlasseId = klasseAktiv.Id, NiveauPreis = 10_000m, StartDatum = DateTime.UtcNow.AddMonths(-1) };
        var entschieden = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = schule.Id, KlasseId = klasseEntschieden.Id, NiveauPreis = 8_000m, StartDatum = DateTime.UtcNow.AddMonths(-12), EndDatum = DateTime.UtcNow.AddMonths(-2) };
        var alt = new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = schule.Id, KlasseId = klasseAlt.Id, NiveauPreis = 5_000m, StartDatum = DateTime.UtcNow.AddMonths(-24), EndDatum = DateTime.UtcNow.AddMonths(-13) };
        db.KursNiveauDurchlaeufe.AddRange(aktiv, entschieden, alt);
        db.KlassenAbschlussEntscheidungen.Add(new KlassenAbschlussEntscheidung { Id = Guid.NewGuid(), DurchlaufId = entschieden.Id, StudentId = kofi.Id });
        db.LevelEnrollments.Add(new LevelEnrollment { Id = Guid.NewGuid(), StudentId = yao.Id, KlasseId = klasseAlt.Id, IstAktuell = false });
        db.RatenZahlungen.Add(new RatenZahlung { Id = Guid.NewGuid(), DurchlaufID = aktiv.Id, StudentId = ama.Id, Betrag = 4_000m, Wirkungsdatum = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var durchlaeufe = await new FinanzService(db).GetDurchlaeufeAsync(schule.Id);

        Assert.Equal([aktiv.Id, entschieden.Id, alt.Id], durchlaeufe.Select(d => d.DurchlaufID));
        var aktivUebersicht = durchlaeufe[0];
        var amaStand = Assert.Single(aktivUebersicht.Schueler);
        Assert.Equal("Ama", amaStand.Name);
        Assert.Equal(4_000m, amaStand.Bezahlt);
        Assert.Equal(10_000m, amaStand.Soll);
        Assert.Equal("Kofi", Assert.Single(durchlaeufe[1].Schueler).Name);
        Assert.Equal("Yao", Assert.Single(durchlaeufe[2].Schueler).Name);
    }

    [Fact]
    public void Finanzen_SchuljahrBeginn_IstDerLetzteErsteSeptember()
    {
        Assert.Equal(new DateTime(2026, 9, 1), Finanzen.SchuljahrBeginn(new DateTime(2026, 11, 13)));
        Assert.Equal(new DateTime(2025, 9, 1), Finanzen.SchuljahrBeginn(new DateTime(2026, 8, 31)));
        Assert.Equal(new DateTime(2026, 9, 1), Finanzen.SchuljahrBeginn(new DateTime(2026, 9, 1)));
    }
}
