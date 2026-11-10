using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SchuelerkartenServiceTests
{
    [Fact]
    public async Task Karten_NurFuerDirektionDerSchuleUndAlsPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var factory = new TestApplicationDbContextFactory();
        var schule = Guid.NewGuid(); var andereSchule = Guid.NewGuid(); var klasse = Guid.NewGuid();
        var direktor = Guid.NewGuid(); var fremdDirektor = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.Schools.AddRange(new School { Id = schule, Name = "Collège Démo", City = "Lomé" }, new School { Id = andereSchule, Name = "Autre école" });
            db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e A" });
            var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
            db.Roles.Add(rolle);
            db.Users.AddRange(new AppUser { Id = direktor, UserName = "direction" }, new AppUser { Id = fremdDirektor, UserName = "autre" });
            db.UserRoles.AddRange(new AppUserRole { UserId = direktor, RoleId = rolle.Id }, new AppUserRole { UserId = fremdDirektor, RoleId = rolle.Id });
            db.UserSchools.AddRange(new UserSchool { AppUserId = direktor, SchoolId = schule, CanManageStudents = true },
                new UserSchool { AppUserId = fremdDirektor, SchoolId = andereSchule, CanManageStudents = true });
            foreach (var (vorname, nachname) in new[] { ("Ama", "Mensah"), ("Kofi", "Agbeko"), ("Yawa", "Lawson") })
            {
                var id = Guid.NewGuid();
                db.Users.Add(new AppUser { Id = id, UserName = $"{vorname}.{nachname}".ToLowerInvariant(), Vorname = vorname, Nachname = nachname, FullName = $"{vorname} {nachname}", GeburtsdatumUtc = new DateTime(2012, 3, 4) });
                db.StudentProfiles.Add(new StudentProfile { Id = Guid.NewGuid(), AppUserId = id, SchoolId = schule, CurrentKlasseId = klasse, Matrikelnummer = $"M-{nachname[..3].ToUpperInvariant()}" });
            }
            await db.SaveChangesAsync();
        }
        var service = new SchuelerkartenService(factory);

        Assert.Null(await service.KlasseAsync(fremdDirektor, klasse));
        var ergebnis = await service.KlasseAsync(direktor, klasse);
        Assert.NotNull(ergebnis);
        Assert.Equal("cartes-6e A.pdf", ergebnis.Value.Dateiname);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(ergebnis.Value.Pdf, 0, 4));

        var ordner = Environment.GetEnvironmentVariable("SKOULOU_BULLETIN_VORSCHAU");
        if (!string.IsNullOrWhiteSpace(ordner))
        {
            Directory.CreateDirectory(ordner);
            File.WriteAllBytes(Path.Combine(ordner, "cartes.pdf"), ergebnis.Value.Pdf);
            var beispiel = SchuelerkartenService.Dokument("Collège Démo Les Palmiers", "Lomé", "2026-2027", null,
                [new("MENSAH Ama", "6e A", "M-MEN", "04/03/2012", null), new("AGBEKO Kofi", "6e A", "M-AGB", "17/08/2011", null)]);
            File.WriteAllBytes(Path.Combine(ordner, "cartes.png"), beispiel.GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings { RasterDpi = 110 }).First());
        }
    }
}
