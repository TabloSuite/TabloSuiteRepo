using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Models.Import;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class SchoolUserImportServiceTests
{
    [Fact]
    public async Task LehrerMitHistorischemVerwaltungsflagDarfNichtImportieren()
    {
        using var db = TestDbContext.CreateInMemory();
        var (user, school, klasse) = await SeedDirectorSchoolAndClassAsync(db);
        db.Roles.Single().Name = Rollen.Lehrer;
        await db.SaveChangesAsync();
        var service = CreateService(db, user);
        var result = await service.CommitAsync(new()
        {
            DirectorId = user.Id, SchoolId = school.Id, Kind = SchoolUserImportKind.Student,
            CsvContent = $"Vorname;Nachname;Klasse\nAfi;Mensah;{klasse.Code}"
        });
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.CreatedCount);
        Assert.Empty(result.Zugangsdaten);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task PreviewAsync_GeneriertEmailWennOptionalesEmailFeldLeerIst()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, klasse) = await SeedDirectorSchoolAndClassAsync(db);
        var service = CreateService(db, director);

        var preview = await service.PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Student,
            CsvContent = $"Vorname;Nachname;Klasse\nAfi;Mensah;{klasse.Code}"
        });

        Assert.True(preview.Succeeded, string.Join(" ", preview.Errors.Concat(preview.Rows.SelectMany(r => r.Errors))));
        var row = Assert.Single(preview.Rows);
        // Keine Platzhalter-Mail mehr: Identifiant wird vorgeschlagen, die E-Mail bleibt leer.
        Assert.True(row.IdentifiantGenerated);
        Assert.Equal("afi.mensah", row.Identifiant);
        Assert.Equal(string.Empty, row.Email);
        Assert.Equal(SchoolUserImportDefaults.InitialPassword, row.InitialPassword);
    }

    [Fact]
    public async Task PreviewAsync_UnbekannteKlasse_MeldetFehler()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, _) = await SeedDirectorSchoolAndClassAsync(db);
        var service = CreateService(db, director);

        var preview = await service.PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Student,
            CsvContent = "Vorname;Nachname;Klasse\nKomi;Adjovi;UNKNOWN"
        });

        Assert.False(preview.Succeeded);
        Assert.Contains(preview.Rows.Single().Errors, error => error.Contains("UNKNOWN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreviewAsync_AkzeptiertFranzoesischeSchuelerHeader()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, klasse) = await SeedDirectorSchoolAndClassAsync(db);
        var service = CreateService(db, director);

        var preview = await service.PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Student,
            CsvContent = $"Prénom;Nom;Classe;E-mail;Téléphone;E-mail parent;Téléphone parent\nKomi;Adjovi;{klasse.Code};komi@example.com;90000000;parent@example.com;91111111"
        });

        Assert.True(preview.Succeeded, string.Join(" ", preview.Errors.Concat(preview.Rows.SelectMany(row => row.Errors))));
        var row = Assert.Single(preview.Rows);
        Assert.Equal("Komi", row.Vorname);
        Assert.Equal("Adjovi", row.Nachname);
        Assert.Equal("komi@example.com", row.Email);
        Assert.Equal("parent@example.com", row.ElternEmail);
        Assert.Equal("91111111", row.ElternTelefon);
    }

    [Fact]
    public async Task PreviewAsync_AkzeptiertFranzoesischeHeaderMitUtf8Bom()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, klasse) = await SeedDirectorSchoolAndClassAsync(db);
        var service = CreateService(db, director);

        var preview = await service.PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Teacher,
            CsvContent = $"\uFEFFPrénom;Nom;Classe;E-mail;Téléphone\nAmah;Abalo;{klasse.Code};amah.abalo@skoulou.com;90000001"
        });

        Assert.True(preview.Succeeded, string.Join(" ", preview.Errors.Concat(preview.Rows.SelectMany(row => row.Errors))));
        var row = Assert.Single(preview.Rows);
        Assert.Equal("Amah", row.Vorname);
        Assert.Equal("Abalo", row.Nachname);
    }

    [Fact]
    public async Task PreviewAsync_AkzeptiertFehldekodiertenFranzoesischenVornamenHeader()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, klasse) = await SeedDirectorSchoolAndClassAsync(db);
        var service = CreateService(db, director);

        var preview = await service.PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Teacher,
            CsvContent = $"PrÃ©nom;Nom;Classe\nAmah;Abalo;{klasse.Code}"
        });

        Assert.True(preview.Succeeded, string.Join(" ", preview.Errors.Concat(preview.Rows.SelectMany(row => row.Errors))));
        Assert.Equal("Amah", Assert.Single(preview.Rows).Vorname);
    }

    [Fact]
    public async Task PreviewAsync_AkzeptiertFranzoesischeLehrerHeader()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, klasse) = await SeedDirectorSchoolAndClassAsync(db);
        var service = CreateService(db, director);

        var preview = await service.PreviewAsync(new SchoolUserImportPreviewRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Teacher,
            CsvContent = $"Prénom;Nom;Classe;E-mail;Téléphone\nAmah;Mensah;{klasse.Code};amah@example.com;92222222"
        });

        Assert.True(preview.Succeeded, string.Join(" ", preview.Errors.Concat(preview.Rows.SelectMany(row => row.Errors))));
        var row = Assert.Single(preview.Rows);
        Assert.Equal("Amah", row.Vorname);
        Assert.Equal("Mensah", row.Nachname);
        Assert.Equal("amah@example.com", row.Email);
        Assert.Equal("92222222", row.Telefon);
    }

    [Fact]
    public async Task CommitAsync_ErstelltLehrerMitMehrerenKlassenZuordnungen()
    {
        using var db = TestDbContext.CreateInMemory();
        var (director, school, firstClass) = await SeedDirectorSchoolAndClassAsync(db);
        var secondClass = new Klasse { Id = Guid.NewGuid(), Code = "CM2B", Title = "CM2 B", SchoolId = school.Id };
        db.Klassen.Add(secondClass);
        await db.SaveChangesAsync();

        var service = CreateService(db, director);

        var result = await service.CommitAsync(new SchoolUserImportCommitRequest
        {
            DirectorId = director.Id,
            SchoolId = school.Id,
            Kind = SchoolUserImportKind.Teacher,
            CsvContent = $"Vorname;Nachname;Klasse\nJean;Koffi;{firstClass.Code},{secondClass.Code}"
        });

        Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        Assert.Equal(1, result.CreatedCount);
        var zugang = Assert.Single(result.Zugangsdaten);
        Assert.NotEqual("MotDePasse1", zugang.Passwort);
        Assert.True(zugang.Passwort.Length >= 20);
        var teacherId = await db.UserSchools
            .Where(link => link.SchoolId == school.Id && link.AppUserId != director.Id)
            .Select(link => link.AppUserId)
            .SingleAsync();
        var assignedClassIds = await db.LehrerNiveauZuordnungen
            .Where(assignment => assignment.LehrerID == teacherId)
            .Select(assignment => assignment.KlasseId)
            .OrderBy(id => id)
            .ToListAsync();
        var expectedClassIds = new[] { firstClass.Id, secondClass.Id }.OrderBy(id => id).ToList();
        Assert.Equal(expectedClassIds, assignedClassIds);
    }

    private static SchoolUserImportService CreateService(TestDbContext db, AppUser director)
    {
        var userManager = TestUserManager.Create([director]);
        userManager.SeedRole(director, Rollen.SchulLeiter);
        var dashboard = new DashboardService(db, userManager, new FakeMediaStorageService());
        return new SchoolUserImportService(db, dashboard);
    }

    private static async Task<(AppUser Director, School School, Klasse Klasse)> SeedDirectorSchoolAndClassAsync(TestDbContext db)
    {
        var director = new AppUser
        {
            Id = Guid.NewGuid(),
            Vorname = "Marie",
            Nachname = "Direction",
            FullName = "Marie Direction",
            Email = "direction@test.local",
            UserName = "direction@test.local"
        };
        var school = new School { Id = Guid.NewGuid(), Name = "École Test", City = "Lomé", IsActive = true };
        var klasse = new Klasse { Id = Guid.NewGuid(), Code = "CM1A", Title = "CM1 A", SchoolId = school.Id };
        db.Users.Add(director);
        var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
        db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { UserId = director.Id, RoleId = rolle.Id });
        db.Schools.Add(school);
        db.Klassen.Add(klasse);
        db.UserSchools.Add(new UserSchool
        {
            AppUserId = director.Id,
            SchoolId = school.Id,
            CanManageStudents = true
        });
        await db.SaveChangesAsync();
        return (director, school, klasse);
    }
}
