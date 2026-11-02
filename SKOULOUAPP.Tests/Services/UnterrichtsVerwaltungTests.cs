using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class UnterrichtsVerwaltungTests
{
    [Fact]
    public async Task SchuljahrSchliessen_SperrtWeitereUnterrichtsaenderungen()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule, "2026", new(2026, 1, 1), new(2026, 12, 31));
        var jahr = Assert.Single((await service.LadenAsync(daten.Verwalter, daten.Schule)).Schuljahre);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SchuljahrSchliessenAsync(daten.Lehrer, daten.Schule, jahr.Id));
        await service.SchuljahrSchliessenAsync(daten.Verwalter, daten.Schule, jahr.Id);
        Assert.True(Assert.Single((await service.LadenAsync(daten.Verwalter, daten.Schule)).Schuljahre).IstGeschlossen);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule, jahr.Id,
            daten.Klasse, daten.Fach, daten.Lehrer, 2));
    }

    [Theory]
    [InlineData("SchulLeiter")]
    [InlineData("Koordinator")]
    public async Task DokumentStammdaten_WerdenGeprueftUndSchulbezogenGespeichert(string rolle)
    {
        var daten = await VorbereitenAsync(rolle);
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await service.DokumentDatenSpeichernAsync(daten.Verwalter, daten.Schule, new SchulDokumentStammdaten
        {
            Slogan = "  Ensemble pour réussir  ", DokumentEmail = "direction@example.org", RepublikZeile = "République togolaise"
        });
        var gespeichert = await service.DokumentDatenLadenAsync(daten.Verwalter, daten.Schule);
        Assert.Equal("Ensemble pour réussir", gespeichert.Slogan);
        Assert.Equal("République togolaise", gespeichert.RepublikZeile);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DokumentDatenSpeichernAsync(daten.Verwalter, daten.Schule,
            new SchulDokumentStammdaten { DokumentEmail = "pas-un-email" }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DokumentDatenSpeichernAsync(daten.Lehrer, daten.Schule, new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DokumentDatenLadenAsync(daten.Verwalter, Guid.NewGuid()));
    }

    [Fact]
    public async Task Matrikelnummer_NormalisiertUndVerhindertDuplikateAuchArchivierterSchueler()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        var erster = Guid.NewGuid();
        var zweiter = Guid.NewGuid();
        await using (var db = daten.Factory.CreateDbContext())
        {
            db.Users.AddRange(new AppUser { Id = erster }, new AppUser { Id = zweiter });
            db.StudentProfiles.AddRange(
                new StudentProfile { Id = Guid.NewGuid(), AppUserId = erster, SchoolId = daten.Schule },
                new StudentProfile { Id = Guid.NewGuid(), AppUserId = zweiter, SchoolId = daten.Schule });
            await db.SaveChangesAsync();
        }
        await service.SchuelerStammdatenSpeichernAsync(daten.Verwalter, daten.Schule, erster, "  abc-123 ", "  Lomé ");
        var gespeichert = (await service.SchuelerStammdatenAsync(daten.Verwalter, daten.Schule)).Single(x => x.SchuelerID == erster);
        Assert.Equal("ABC-123", gespeichert.Matrikelnummer);
        Assert.Equal("Lomé", gespeichert.Geburtsort);
        await using (var db = daten.Factory.CreateDbContext())
        {
            (await db.StudentProfiles.SingleAsync(x => x.AppUserId == erster)).IsArchived = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchuelerStammdatenSpeichernAsync(daten.Verwalter, daten.Schule, zweiter, "abc-123", null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SchuelerStammdatenSpeichernAsync(daten.Lehrer, daten.Schule, zweiter, "X", null));
        await service.SchuelerStammdatenSpeichernAsync(daten.Verwalter, daten.Schule, zweiter, " ", " ");
        var leer = Assert.Single(await service.SchuelerStammdatenAsync(daten.Verwalter, daten.Schule));
        Assert.Equal($"EL-{zweiter:N}", leer.Matrikelnummer);
        Assert.Null(leer.Geburtsort);
    }

    [Fact]
    public async Task SchuelerStammdaten_VerhindernFremdenSchuelerUndUeberlangeNummer()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchuelerStammdatenSpeichernAsync(daten.Verwalter, daten.Schule, Guid.NewGuid(), "A", null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchuelerStammdatenSpeichernAsync(daten.Verwalter, daten.Schule, Guid.NewGuid(), new string('A', 61), null));
    }

    [Theory]
    [InlineData("SchulLeiter")]
    [InlineData("Koordinator")]
    public async Task Verwalter_KannSchuljahrUndUnterrichtAnlegen(string rolle)
    {
        var daten = await VorbereitenAsync(rolle);
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule, "2026–2027", new(2026, 9, 1), new(2027, 7, 31));
        var jahr = Assert.Single((await service.LadenAsync(daten.Verwalter, daten.Schule)).Schuljahre);
        await service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule, jahr.Id, daten.Klasse, daten.Fach, daten.Lehrer, 3.5m);
        await service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule, jahr.Id, daten.Klasse, daten.Fach, daten.Lehrer, 4m);
        var zeile = Assert.Single((await service.LadenAsync(daten.Verwalter, daten.Schule)).Unterricht);
        Assert.Equal(4m, zeile.Wochenstunden);
        await using var db = daten.Factory.CreateDbContext();
        Assert.Equal(1m, (await db.LehrerNiveauZuordnungen.SingleAsync()).Wochenstunden);
    }

    [Fact]
    public async Task FremdeSchuleUndLehrerrolle_WerdenAbgewiesen()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LadenAsync(daten.Verwalter, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LadenAsync(daten.Lehrer, daten.Schule));
        Assert.Empty(await service.SchulenAsync(daten.Lehrer));
    }

    [Fact]
    public async Task GesperrterVerwalter_WirdAbgewiesen()
    {
        var daten = await VorbereitenAsync();
        await using (var db = daten.Factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == daten.Verwalter)).IsBlocked = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new UnterrichtsVerwaltungService(daten.Factory)
            .LadenAsync(daten.Verwalter, daten.Schule));
    }

    [Fact]
    public async Task UngueltigeUndUeberlappendeSchuljahre_WerdenAbgewiesen()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule,
            "", new(2027, 1, 1), new(2026, 1, 1)));
        await service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule, "2026", new(2026, 1, 1), new(2026, 12, 31));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule,
            "2027", new(2026, 12, 1), new(2027, 12, 31)));
    }

    [Fact]
    public async Task Unterricht_VerhindertFremdeFaecherFehlendeZuordnungUndGeschlosseneJahre()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        await service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule, "2026", new(2026, 1, 1), new(2026, 12, 31));
        var jahr = Assert.Single((await service.LadenAsync(daten.Verwalter, daten.Schule)).Schuljahre);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule,
            jahr.Id, daten.Klasse, Guid.NewGuid(), daten.Lehrer, 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule,
            jahr.Id, daten.Klasse, daten.Fach, daten.Lehrer, -1));
        await using (var db = daten.Factory.CreateDbContext())
        {
            db.LehrerFachZuordnungen.Remove(await db.LehrerFachZuordnungen.SingleAsync());
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule,
            jahr.Id, daten.Klasse, daten.Fach, daten.Lehrer, 2));
        await using (var db = daten.Factory.CreateDbContext())
        {
            (await db.Set<Schuljahr>().SingleAsync()).IstGeschlossen = true;
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UnterrichtSpeichernAsync(daten.Verwalter, daten.Schule,
            jahr.Id, daten.Klasse, daten.Fach, daten.Lehrer, 2));
    }

    [Fact]
    public async Task JahresZuordnung_BehaeltKlasseUndWiederholerstatus()
    {
        var daten = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(daten.Factory);
        var schueler = Guid.NewGuid();
        await using (var db = daten.Factory.CreateDbContext())
        {
            db.Users.Add(new AppUser { Id = schueler });
            db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schueler, SchoolId = daten.Schule, CurrentKlasseId = daten.Klasse });
            await db.SaveChangesAsync();
        }
        await service.SchuljahrSpeichernAsync(daten.Verwalter, daten.Schule, "2026", new(2026, 1, 1), new(2026, 12, 31));
        var jahr = Assert.Single((await service.LadenAsync(daten.Verwalter, daten.Schule)).Schuljahre);
        await service.SchuelerZuordnenAsync(daten.Verwalter, daten.Schule, jahr.Id, daten.Klasse, schueler, true);
        await service.SchuelerZuordnenAsync(daten.Verwalter, daten.Schule, jahr.Id, daten.Klasse, schueler, true);
        await using var kontrolle = daten.Factory.CreateDbContext();
        var zuordnung = Assert.Single(await kontrolle.Set<SchuelerJahresZuordnung>().ToListAsync());
        Assert.True(zuordnung.IstWiederholer);
        Assert.Equal(daten.Klasse, zuordnung.KlasseID);
    }

    [Fact]
    public async Task AutomatischeMatrikelnummer_BleibtStabilUndErgaenztNurFehlende()
    {
        var d = await VorbereitenAsync();
        var service = new UnterrichtsVerwaltungService(d.Factory);
        Guid schueler;
        await using (var db = d.Factory.CreateDbContext())
        {
            schueler = Guid.NewGuid();
            db.Users.Add(new AppUser { Id = schueler });
            db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schueler, SchoolId = d.Schule });
            await db.SaveChangesAsync();
            Assert.Equal($"EL-{schueler:N}", (await db.StudentProfiles.SingleAsync()).Matrikelnummer);
        }
        await service.SchuelerStammdatenSpeichernAsync(d.Verwalter, d.Schule, schueler, "MANUEL-1", null);
        await service.SchuelerStammdatenSpeichernAsync(d.Verwalter, d.Schule, schueler, null, "Lomé");
        Assert.Equal("MANUEL-1", Assert.Single(await service.SchuelerStammdatenAsync(d.Verwalter, d.Schule)).Matrikelnummer);
        await using (var db = d.Factory.CreateDbContext())
        {
            (await db.StudentProfiles.SingleAsync()).Matrikelnummer = null; await db.SaveChangesAsync();
        }
        Assert.Equal(1, await service.MatrikelnummernErgaenzenAsync(d.Verwalter, d.Schule));
        Assert.Equal(0, await service.MatrikelnummernErgaenzenAsync(d.Verwalter, d.Schule));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.MatrikelnummernErgaenzenAsync(d.Lehrer, d.Schule));
    }

    private static async Task<TestDaten> VorbereitenAsync(string rolle = "SchulLeiter")
    {
        var factory = new TestApplicationDbContextFactory();
        await using var db = factory.CreateDbContext();
        var schule = new School { Id = Guid.NewGuid(), Name = "École" };
        var verwalter = new AppUser { Id = Guid.NewGuid(), FullName = "Direction" };
        var lehrer = new AppUser { Id = Guid.NewGuid(), FullName = "Enseignant" };
        var verwalterRolle = new Role { Id = Guid.NewGuid(), Name = rolle };
        var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = "Lehrer" };
        var klasse = new Klasse { Id = Guid.NewGuid(), SchoolId = schule.Id, Code = "6e" };
        var fach = new Fach { Id = Guid.NewGuid(), SchoolId = schule.Id, Name = "Mathématiques" };
        db.AddRange(schule, verwalter, lehrer, verwalterRolle, lehrerRolle, klasse, fach);
        db.UserRoles.AddRange(new AppUserRole { UserId = verwalter.Id, RoleId = verwalterRolle.Id },
            new AppUserRole { UserId = lehrer.Id, RoleId = lehrerRolle.Id });
        db.UserSchools.AddRange(new UserSchool { AppUserId = verwalter.Id, SchoolId = schule.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = schule.Id });
        db.LehrerNiveauZuordnungen.Add(new LehrerNiveauZuordnung { Id = Guid.NewGuid(), LehrerID = lehrer.Id,
            SchulID = schule.Id, KlasseId = klasse.Id, Wochenstunden = 1 });
        db.LehrerFachZuordnungen.Add(new LehrerFachZuordnung { Id = Guid.NewGuid(), LehrerId = lehrer.Id, FachId = fach.Id });
        await db.SaveChangesAsync();
        return new(factory, verwalter.Id, schule.Id, klasse.Id, fach.Id, lehrer.Id);
    }

    private sealed record TestDaten(TestApplicationDbContextFactory Factory, Guid Verwalter, Guid Schule, Guid Klasse, Guid Fach, Guid Lehrer);
}
