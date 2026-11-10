using System.Text.Json;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Eltern-Zugang: Anlage durch die Direktion, Verknüpfung nur innerhalb der Schule, Sicht nur auf eigene Kinder.</summary>
public class ElternServiceTests
{
    [Fact]
    public async Task Eltern_SehenNurVerknuepfteKinderIhrerSchule()
    {
        var factory = new TestApplicationDbContextFactory();
        var schule = Guid.NewGuid(); var fremdeSchule = Guid.NewGuid(); var klasse = Guid.NewGuid();
        var direktor = new AppUser { Id = Guid.NewGuid(), UserName = "direction" };
        var kind1 = Guid.NewGuid(); var kind2 = Guid.NewGuid(); var fremdesKind = Guid.NewGuid(); var fremderElternteil = Guid.NewGuid();
        var jahr = Guid.NewGuid(); var periode = Guid.NewGuid(); var zuordnung = Guid.NewGuid(); var ausgabe = Guid.NewGuid();
        var durchlauf = Guid.NewGuid(); var zahlung = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Schools.AddRange(new School { Id = schule, Name = "Collège Démo", City = "Lomé" }, new School { Id = fremdeSchule, Name = "Autre" });
            db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e A" });
            var leiter = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            db.Roles.Add(leiter);
            db.Users.Add(direktor);
            db.UserRoles.Add(new AppUserRole { UserId = direktor.Id, RoleId = leiter.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = direktor.Id, SchoolId = schule, CanManageStudents = true });
            foreach (var (id, name, schul) in new[] { (kind1, "Ama Mensah", schule), (kind2, "Kofi Mensah", schule), (fremdesKind, "Yao Autre", fremdeSchule) })
            {
                db.Users.Add(new AppUser { Id = id, UserName = name.Replace(' ', '.').ToLowerInvariant(), FullName = name, Nachname = name.Split(' ')[1], Vorname = name.Split(' ')[0] });
                db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = id, SchoolId = schul, CurrentKlasseId = schul == schule ? klasse : null });
            }
            db.Users.Add(new AppUser { Id = fremderElternteil, UserName = "autre.parent" });
            db.Add(new Schuljahr { Id = jahr, SchulID = schule, Bezeichnung = "2026-2027", Beginn = DateOnly.FromDateTime(DateTime.Now.AddMonths(-2)), Ende = DateOnly.FromDateTime(DateTime.Now.AddMonths(8)) });
            db.Add(new BewertungsPeriode { Id = periode, SchulID = schule, SchuljahrID = jahr, Titel = "1er trimestre", Art = BewertungsArt.Trimester, Reihenfolge = 1 });
            db.Add(new SchuelerJahresZuordnung { Id = zuordnung, SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, SchuelerID = kind1 });
            db.Add(new BulletinAusgabe { Id = ausgabe, SchulID = schule, PeriodeID = periode, JahresZuordnungID = zuordnung, Version = 1, IstVeroeffentlicht = true,
                ErstelltVonID = direktor.Id, SnapshotJson = JsonSerializer.Serialize(BulletinBeispiel.Erzeugen(false, new BulletinLayout(true, true, true, true, false, true, true, true, true, false, false))) });
            db.Add(new AnwesenheitsEintrag { Id = Guid.NewGuid(), PeriodeID = periode, JahresZuordnungID = zuordnung, Datum = DateOnly.FromDateTime(DateTime.Now), Status = AnwesenheitsStatus.Abwesend, BearbeitetVonID = direktor.Id });
            db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf { Id = durchlauf, SchulID = schule, KlasseId = klasse, NiveauPreis = 60000m, StartDatum = DateTime.Today.AddMonths(-1), ErstelltVonUserID = direktor.Id });
            db.Add(new RatenZahlung { Id = zahlung, DurchlaufID = durchlauf, StudentId = kind1, Betrag = 20000m, Wirkungsdatum = DateTime.Today, ErfasstVonUserID = direktor.Id });
            await db.SaveChangesAsync();
        }

        var manager = TestUserManager.Create([direktor]);
        manager.SeedRole(direktor, Rollen.SchulLeiter);
        manager.OnUserCreated = user => { using var db = factory.CreateDbContext(); db.Users.Add(user); db.SaveChanges(); };
        var dashboard = new DashboardService(factory.CreateDbContext(), manager, new FakeMediaStorageService());
        var service = new ElternService(factory, dashboard, manager);

        var (ok, meldung, zugang) = await service.AnlegenAsync(direktor.Id, kind1, new ElternEingabe("Afi", "Mensah", null, null, "90000000", "Mère"));
        Assert.True(ok, meldung);
        Assert.Equal("afi.mensah", zugang!.Identifiant);
        Assert.False(string.IsNullOrWhiteSpace(zugang.Passwort));
        var elternID = (await manager.FindByNameAsync("afi.mensah"))!.Id;

        // Geschwisterkind verknüpfen; Kind einer anderen Schule wird abgelehnt.
        await service.VerknuepfenAsync(direktor.Id, elternID, kind2, "Mère");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VerknuepfenAsync(direktor.Id, elternID, fremdesKind, "Mère"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VerknuepfenAsync(elternID, elternID, kind2, null));

        var kinder = await service.KinderAsync(elternID);
        Assert.Equal(2, kinder.Count);
        var ama = kinder.Single(k => k.SchuelerID == kind1);
        Assert.Single(ama.Bulletins);
        Assert.Equal(1, ama.Abwesenheiten);
        var gebuehren = Assert.Single(ama.Gebuehren);
        Assert.Equal(20000m, gebuehren.Bezahlt);
        Assert.Equal(40000m, gebuehren.Rest);
        Assert.Empty(await service.KinderAsync(fremderElternteil));

        // Bulletin-PDF und Reçu: Elternteil ja, fremder Elternteil nein.
        await using (var db = factory.CreateDbContext())
        {
            db.UserSchools.Add(new UserSchool { AppUserId = fremderElternteil, SchoolId = schule });
            await db.SaveChangesAsync();
        }
        var bulletins = new BulletinService(factory, new BewertungsService(factory));
        Assert.NotNull(await bulletins.LesenAsync(elternID, ausgabe));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bulletins.LesenAsync(fremderElternteil, ausgabe));
        var quittungen = new QuittungService(factory);
        Assert.NotNull(await quittungen.LadenAsync(zahlung, elternID));
        Assert.Null(await quittungen.LadenAsync(zahlung, fremderElternteil));

        // Trennen und neues Passwort.
        await service.TrennenAsync(direktor.Id, elternID, kind2);
        Assert.Single(await service.KinderAsync(elternID));
        var neu = await service.NeuesPasswortAsync(direktor.Id, elternID);
        Assert.Equal("afi.mensah", neu.Identifiant);

        var uebersicht = await service.UebersichtAsync(direktor.Id);
        Assert.Single(uebersicht.Single(s => s.SchuelerID == kind1).Eltern);
        Assert.Empty(uebersicht.Single(s => s.SchuelerID == kind2).Eltern);
        Assert.DoesNotContain(uebersicht, s => s.SchuelerID == fremdesKind);
    }
}
