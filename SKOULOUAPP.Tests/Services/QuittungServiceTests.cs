using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class QuittungServiceTests
{
    [Theory]
    [InlineData(25000, "vingt-cinq mille")]
    [InlineData(1500, "mille cinq cents")]
    [InlineData(80000, "quatre-vingt mille")]
    [InlineData(200000, "deux cent mille")]
    [InlineData(1250000, "un million deux cent cinquante mille")]
    [InlineData(71, "soixante et onze")]
    [InlineData(1001, "mille un")]
    public void BetragInWorten_SchreibtFrancsCfaAus(decimal betrag, string erwartet)
        => Assert.Equal(erwartet, QuittungService.BetragInWorten(betrag));

    [Fact]
    public async Task Quittung_ZeigtStandNachDieserZahlungUndPrueftZugriff()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var factory = new TestApplicationDbContextFactory();
        var schule = Guid.NewGuid(); var klasse = Guid.NewGuid(); var durchlauf = Guid.NewGuid();
        var direktor = Guid.NewGuid(); var schueler = Guid.NewGuid(); var fremd = Guid.NewGuid();
        var erste = Guid.NewGuid(); var zweite = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Schools.Add(new School { Id = schule, Name = "Collège Démo", City = "Lomé", IsActive = true });
            db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e A", Title = "Sixième A" });
            var leiterRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            var schuelerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Schueler };
            db.Roles.AddRange(leiterRolle, schuelerRolle);
            db.Users.AddRange(
                new AppUser { Id = direktor, UserName = "direction", FullName = "Afi Lawson" },
                new AppUser { Id = schueler, UserName = "kossi.mensah", FullName = "Kossi Mensah" },
                new AppUser { Id = fremd, UserName = "fremd", FullName = "Autre" });
            db.UserRoles.AddRange(new AppUserRole { UserId = direktor, RoleId = leiterRolle.Id }, new AppUserRole { UserId = schueler, RoleId = schuelerRolle.Id });
            db.UserSchools.AddRange(
                new UserSchool { AppUserId = direktor, SchoolId = schule, CanManageStudents = true },
                new UserSchool { AppUserId = schueler, SchoolId = schule });
            db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = schueler, SchoolId = schule, CurrentKlasseId = klasse, Matrikelnummer = "M-001" });
            db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf { Id = durchlauf, SchulID = schule, KlasseId = klasse, NiveauPreis = 75000m, StartDatum = new DateTime(2026, 9, 1), ErstelltVonUserID = direktor });
            db.Add(new RatenZahlung { Id = erste, DurchlaufID = durchlauf, StudentId = schueler, Betrag = 25000m, Wirkungsdatum = new DateTime(2026, 9, 15), ErfasstVonUserID = direktor });
            db.Add(new RatenZahlung { Id = zweite, DurchlaufID = durchlauf, StudentId = schueler, Betrag = 30000m, Wirkungsdatum = new DateTime(2026, 11, 2), ErfasstVonUserID = direktor });
            await db.SaveChangesAsync();
        }
        var service = new QuittungService(factory);

        var ersteQuittung = await service.LadenAsync(erste, direktor);
        Assert.NotNull(ersteQuittung);
        Assert.Equal(25000m, ersteQuittung.BisherBezahlt);
        Assert.Equal(50000m, ersteQuittung.Rest);

        var zweiteQuittung = await service.LadenAsync(zweite, schueler);
        Assert.NotNull(zweiteQuittung);
        Assert.Equal(55000m, zweiteQuittung.BisherBezahlt);
        Assert.Equal(20000m, zweiteQuittung.Rest);
        Assert.Equal("M-001", zweiteQuittung.Matrikel);
        Assert.StartsWith("REC-2026-", zweiteQuittung.Nummer);

        Assert.Null(await service.LadenAsync(zweite, fremd));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(service.Pdf(zweiteQuittung), 0, 4));
        var ordner = Environment.GetEnvironmentVariable("SKOULOU_BULLETIN_VORSCHAU");
        if (!string.IsNullOrWhiteSpace(ordner))
        {
            Directory.CreateDirectory(ordner);
            File.WriteAllBytes(Path.Combine(ordner, "recu.png"), service.Dokument(zweiteQuittung).GenerateImages(new ImageGenerationSettings { RasterDpi = 110 }).First());
        }
    }
}
