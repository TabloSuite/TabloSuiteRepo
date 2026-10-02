using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für die CECR-basierten Niveaugruppen: Ein Schüler darf ausschließlich
/// Niveaus seiner eigenen CECR-Gruppe belegen (z. B. nur A1.1 und A1.2 bei CECR "A1").
/// </summary>
public class NiveauGruppenServiceTests
{
    private static (AppUser Student, Klasse A11, Klasse A12, Klasse A21) SeedSprachschule(
        TestDbContext db,
        bool mitGruppe)
    {
        var schule = new School { Id = Guid.NewGuid(), Name = "Sprachschule", IsActive = true };
        var student = new AppUser { Id = Guid.NewGuid(), FullName = "Schüler", UserName = "schueler" };

        var a11 = new Klasse { Id = Guid.NewGuid(), Code = "A1.1", Title = "A1.1", CEFRGroup = "A1", IstSystemNiveau = true };
        var a12 = new Klasse { Id = Guid.NewGuid(), Code = "A1.2", Title = "A1.2", CEFRGroup = "A1", IstSystemNiveau = true };
        var a21 = new Klasse { Id = Guid.NewGuid(), Code = "A2.1", Title = "A2.1", CEFRGroup = "A2", IstSystemNiveau = true };

        var profil = new StudentProfile
        {
            Id = Guid.NewGuid(),
            AppUserId = student.Id,
            SchoolId = schule.Id
        };

        db.Schools.Add(schule);
        db.Users.Add(student);
        db.Klassen.AddRange(a11, a12, a21);

        if (mitGruppe)
        {
            // Die aktuelle Klasse bestimmt über ihr CECR die erlaubte Gruppe.
            profil.CurrentKlasseId = a11.Id;
            profil.CurrentLevelCode = a11.Code;
        }

        db.StudentProfiles.Add(profil);
        db.SaveChanges();

        return (student, a11, a12, a21);
    }

    private static async Task StarteAktivenDurchlaufAsync(TestDbContext db, Guid schulId, Klasse klasse)
    {
        db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf
        {
            Id = Guid.NewGuid(),
            SchulID = schulId,
            KlasseId = klasse.Id,
            NiveauPreis = 12500m,
            StartDatum = DateTime.UtcNow.AddDays(-1),
            EndDatum = null,
            ErstelltVonUserID = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task IstKlasseErlaubtAsync_ErlaubtNiveausDerEigenenGruppe()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, a11, a12, _) = SeedSprachschule(db, mitGruppe: true);
        var service = new NiveauGruppenService(db);

        Assert.True(await service.IstKlasseErlaubtAsync(student.Id, a11.Id));
        Assert.True(await service.IstKlasseErlaubtAsync(student.Id, a12.Id));
    }

    [Fact]
    public async Task IstKlasseErlaubtAsync_LehntNiveauEinerAnderenGruppeAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, a21) = SeedSprachschule(db, mitGruppe: true);
        var service = new NiveauGruppenService(db);

        Assert.False(await service.IstKlasseErlaubtAsync(student.Id, a21.Id));
    }

    [Fact]
    public async Task IstKlasseErlaubtAsync_OhneGruppeBleibtOhneEinschraenkung()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, a21) = SeedSprachschule(db, mitGruppe: false);
        var service = new NiveauGruppenService(db);

        Assert.True(await service.IstKlasseErlaubtAsync(student.Id, a21.Id));
    }

    [Fact]
    public async Task IstKlasseErlaubtAsync_ErlaubtSelbstAngelegteKlasseDerEigenenSchule()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, _) = SeedSprachschule(db, mitGruppe: true);

        // Die selbst angelegte Prüfungsvorbereitung hat keine CECR-Gruppe
        // und muss trotzdem für Schüler mit Gruppe belegbar sein.
        var profil = db.StudentProfiles.Single(x => x.AppUserId == student.Id);
        var pruefungsvorbereitung = new Klasse
        {
            Id = Guid.NewGuid(),
            Code = "PREP",
            Title = "Préparation à l'examen",
            SchoolId = profil.SchoolId,
            IstSystemNiveau = false
        };
        db.Klassen.Add(pruefungsvorbereitung);
        db.SaveChanges();

        var service = new NiveauGruppenService(db);

        Assert.True(await service.IstKlasseErlaubtAsync(student.Id, pruefungsvorbereitung.Id));
    }

    [Fact]
    public async Task IstKlasseErlaubtAsync_LehntSelbstAngelegteKlasseEinerFremdenSchuleAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, _) = SeedSprachschule(db, mitGruppe: true);

        var fremdeSchule = new School { Id = Guid.NewGuid(), Name = "Andere Schule", IsActive = true };
        var fremdeKlasse = new Klasse
        {
            Id = Guid.NewGuid(),
            Code = "PREP-X",
            Title = "Fremde Klasse",
            SchoolId = fremdeSchule.Id,
            IstSystemNiveau = false
        };
        db.Schools.Add(fremdeSchule);
        db.Klassen.Add(fremdeKlasse);
        db.SaveChanges();

        var service = new NiveauGruppenService(db);

        Assert.False(await service.IstKlasseErlaubtAsync(student.Id, fremdeKlasse.Id));
    }

    [Fact]
    public async Task AboZuweisenAsync_LehntNiveauAusserhalbDerGruppeAb()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, _, a21) = SeedSprachschule(db, mitGruppe: true);
        var schulId = db.StudentProfiles.Single(x => x.AppUserId == student.Id).SchoolId;
        await StarteAktivenDurchlaufAsync(db, schulId, a21);
        var service = new AboService(db);

        var (succeeded, message) = await service.AboZuweisenAsync(student.Id, a21.Id, AboTyp.SkoulouApp);

        Assert.False(succeeded);
        Assert.Equal(NiveauGruppenService.NichtErlaubtMeldung, message);
        Assert.Empty(db.LevelEnrollments);
    }

    [Fact]
    public async Task AboZuweisenAsync_ErlaubtNiveauInnerhalbDerGruppe()
    {
        using var db = TestDbContext.CreateInMemory();
        var (student, _, a12, _) = SeedSprachschule(db, mitGruppe: true);
        var schulId = db.StudentProfiles.Single(x => x.AppUserId == student.Id).SchoolId;
        await StarteAktivenDurchlaufAsync(db, schulId, a12);
        var service = new AboService(db);

        var (succeeded, _) = await service.AboZuweisenAsync(student.Id, a12.Id, AboTyp.SkoulouApp);

        Assert.True(succeeded);
        Assert.Single(db.LevelEnrollments);
    }
}
