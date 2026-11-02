using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class StundenplanTests
{
    [Fact]
    public async Task KopieBleibtEntwurfUndWochenstundenSindMinutenbasiert()
    {
        var d = await DatenAsync();
        var service = new StundenplanService(d.Factory);
        var plan = await service.AnlegenAsync(d.Direktor, d.Schule, d.Jahr, d.Klasse, "Original", new(2026, 9, 1), new(2027, 7, 1));
        await service.EintragSpeichernAsync(d.Direktor, plan, d.Unterricht, DayOfWeek.Monday, new(8, 0), new(9, 30), "A");
        await service.VeroeffentlichenAsync(d.Direktor, plan, true);
        var kopie = await service.KopierenAsync(d.Direktor, plan, "Copie", new(2026, 9, 1), new(2027, 7, 1));
        var volumen = Assert.Single(await service.WochenstundenAsync(d.Direktor, kopie));
        Assert.Equal(1.5m, volumen.Ist);
        Assert.Equal(2m, volumen.Soll);
        Assert.False((await service.PlaeneAsync(d.Direktor, d.Schule)).Single(x => x.Id == kopie).IstVeroeffentlicht);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VeroeffentlichenAsync(d.Direktor, kopie, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.KopierenAsync(d.Lehrer, plan, "Copie", new(2026, 9, 1), new(2027, 7, 1)));
    }

    [Fact]
    public void Pdf_ErzeugtEineEchtePdfDatei()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = new StundenplanPdfService().Erzeugen([
            new StundenplanZeile(Guid.NewGuid(), Guid.NewGuid(), "Planning", "6e", "Mathématiques", "Enseignant",
                DayOfWeek.Monday, new(8, 0), new(9, 0), "Salle 1", new(2026, 9, 1), new(2027, 7, 1), true)
        ]);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public async Task Planung_VerhindertUeberlappungUndErlaubtAngrenzendeStunden()
    {
        var d = await DatenAsync();
        var service = new StundenplanService(d.Factory);
        var plan = await service.AnlegenAsync(d.Direktor, d.Schule, d.Jahr, d.Klasse, "Plan", new(2026, 9, 1), new(2027, 7, 1));
        await service.EintragSpeichernAsync(d.Direktor, plan, d.Unterricht, DayOfWeek.Monday, new(8, 0), new(9, 0), "A");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EintragSpeichernAsync(d.Direktor, plan, d.Unterricht,
            DayOfWeek.Monday, new(8, 30), new(9, 30), "B"));
        await service.EintragSpeichernAsync(d.Direktor, plan, d.Unterricht, DayOfWeek.Monday, new(9, 0), new(10, 0), "A");
        Assert.Equal(2, (await service.LesenAsync(d.Direktor, d.Schule)).Count);
    }

    [Fact]
    public async Task Veroeffentlichung_SchuetztBearbeitungUndZeigtNurBerechtigteDaten()
    {
        var d = await DatenAsync();
        var service = new StundenplanService(d.Factory);
        var plan = await service.AnlegenAsync(d.Direktor, d.Schule, d.Jahr, d.Klasse, "Plan", new(2026, 9, 1), new(2027, 7, 1));
        Assert.Empty(await service.LesenAsync(d.Lehrer, d.Schule));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.VeroeffentlichenAsync(d.Direktor, plan, true));
        await service.EintragSpeichernAsync(d.Direktor, plan, d.Unterricht, DayOfWeek.Tuesday, new(8, 0), new(9, 0), null);
        Assert.Empty(await service.LesenAsync(d.Schueler, d.Schule));
        await service.VeroeffentlichenAsync(d.Direktor, plan, true);
        Assert.Single(await service.LesenAsync(d.Lehrer, d.Schule));
        Assert.Single(await service.LesenAsync(d.Schueler, d.Schule));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EintragSpeichernAsync(d.Direktor, plan, d.Unterricht,
            DayOfWeek.Wednesday, new(8, 0), new(9, 0), null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PlaeneAsync(d.Lehrer, d.Schule));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LesenAsync(d.Direktor, Guid.NewGuid()));
        await service.VeroeffentlichenAsync(d.Direktor, plan, false);
        Assert.Empty(await service.LesenAsync(d.Schueler, d.Schule));
    }

    [Fact]
    public async Task LehrerUndRaumKonflikte_WerdenZwischenKlassenGeprueft()
    {
        var d = await DatenAsync();
        var service = new StundenplanService(d.Factory);
        var erster = await service.AnlegenAsync(d.Direktor, d.Schule, d.Jahr, d.Klasse, "Premier", new(2026, 9, 1), new(2027, 7, 1));
        await service.EintragSpeichernAsync(d.Direktor, erster, d.Unterricht, DayOfWeek.Monday, new(8, 0), new(9, 0), "Salle 1");
        await service.VeroeffentlichenAsync(d.Direktor, erster, true);
        var klasse = Guid.NewGuid();
        var unterricht = Guid.NewGuid();
        await using (var db = d.Factory.CreateDbContext())
        {
            var quelle = await db.Set<UnterrichtsZuordnung>().SingleAsync();
            db.Klassen.Add(new Klasse { Id = klasse, SchoolId = d.Schule, Code = "5e" });
            db.Add(new UnterrichtsZuordnung { Id = unterricht, SchulID = d.Schule, SchuljahrID = d.Jahr,
                KlasseID = klasse, FachID = quelle.FachID, LehrerID = d.Lehrer, Wochenstunden = 2 });
            await db.SaveChangesAsync();
        }
        var zweiter = await service.AnlegenAsync(d.Direktor, d.Schule, d.Jahr, klasse, "Deuxième", new(2026, 9, 1), new(2027, 7, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EintragSpeichernAsync(d.Direktor, zweiter, unterricht,
            DayOfWeek.Monday, new(8, 0), new(9, 0), "Autre salle"));
        await service.EintragSpeichernAsync(d.Direktor, zweiter, unterricht, DayOfWeek.Tuesday, new(8, 0), new(9, 0), "Salle 1");
    }

    private static async Task<Daten> DatenAsync()
    {
        var f = new TestApplicationDbContextFactory();
        await using var db = f.CreateDbContext();
        var schule = Guid.NewGuid(); var direktor = Guid.NewGuid(); var lehrer = Guid.NewGuid(); var schueler = Guid.NewGuid();
        var jahr = Guid.NewGuid(); var klasse = Guid.NewGuid(); var fach = Guid.NewGuid(); var unterricht = Guid.NewGuid();
        db.Schools.Add(new School { Id = schule, Name = "École" });
        db.Users.AddRange(new AppUser { Id = direktor }, new AppUser { Id = lehrer }, new AppUser { Id = schueler });
        foreach (var (id, name) in new[] { (direktor, Rollen.SchulLeiter), (lehrer, Rollen.Lehrer), (schueler, Rollen.Schueler) })
        {
            var rolle = new Role { Id = Guid.NewGuid(), Name = name };
            db.Roles.Add(rolle);
            db.UserRoles.Add(new AppUserRole { UserId = id, RoleId = rolle.Id });
            db.UserSchools.Add(new UserSchool { AppUserId = id, SchoolId = schule });
        }
        db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e" });
        db.Faecher.Add(new Fach { Id = fach, SchoolId = schule, Name = "Mathématiques" });
        db.Add(new Schuljahr { Id = jahr, SchulID = schule, Bezeichnung = "2026–2027", Beginn = new(2026, 9, 1), Ende = new(2027, 7, 31) });
        db.Add(new UnterrichtsZuordnung { Id = unterricht, SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, FachID = fach, LehrerID = lehrer, Wochenstunden = 2 });
        db.Add(new SchuelerJahresZuordnung { Id = Guid.NewGuid(), SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, SchuelerID = schueler });
        await db.SaveChangesAsync();
        return new(f, schule, direktor, lehrer, schueler, jahr, klasse, unterricht);
    }

    private sealed record Daten(TestApplicationDbContextFactory Factory, Guid Schule, Guid Direktor, Guid Lehrer, Guid Schueler,
        Guid Jahr, Guid Klasse, Guid Unterricht);
}
