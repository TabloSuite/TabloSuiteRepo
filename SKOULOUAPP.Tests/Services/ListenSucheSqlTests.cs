using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Email;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Opt-in: SKOULOU_SQL_TESTS=1. Prüft, dass die serverseitigen Listen-Abfragen (Suche, Skip/Take, Zählungen)
/// von SQL Server übersetzt werden. Ausschließlich zufällig benannte lokale Testdatenbank.
/// </summary>
public class ListenSucheSqlTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task SqlServer_ListenAbfragenWerdenUebersetzt()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        var name = "SKOULOUAPP_TEST_" + Guid.NewGuid().ToString("N");
        var factory = new SqlFactory(name);
        try
        {
            await using (var schema = factory.CreateDbContext()) await schema.Database.MigrateAsync();
            var schule = Guid.NewGuid(); var admin = Guid.NewGuid(); var lehrer = Guid.NewGuid(); var direktor = Guid.NewGuid();
            var klasse = Guid.NewGuid();
            await using (var db = factory.CreateDbContext())
            {
                db.Schools.Add(new School { Id = schule, Name = "SQL-Test" });
                var adminRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SuperAdmin, NormalizedName = "SUPERADMIN" };
                var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer, NormalizedName = "LEHRER" };
                var direktorRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter, NormalizedName = "SCHULLEITER" };
                db.Roles.AddRange(adminRolle, lehrerRolle, direktorRolle);
                db.Users.AddRange(
                    new AppUser { Id = admin, UserName = "admin.sql", Email = "admin@sql.test", FullName = "Admin" },
                    new AppUser { Id = lehrer, UserName = "lehrer.sql", Email = "lehrer@sql.test", FullName = "Anna Lehrer" },
                    new AppUser { Id = direktor, UserName = "direktor.sql", Email = "direktor@sql.test", FullName = "Zoe Direktor" });
                db.UserRoles.AddRange(
                    new AppUserRole { UserId = admin, RoleId = adminRolle.Id },
                    new AppUserRole { UserId = lehrer, RoleId = lehrerRolle.Id },
                    new AppUserRole { UserId = direktor, RoleId = lehrerRolle.Id },
                    new AppUserRole { UserId = direktor, RoleId = direktorRolle.Id });
                db.UserSchools.AddRange(new UserSchool { AppUserId = lehrer, SchoolId = schule }, new UserSchool { AppUserId = direktor, SchoolId = schule });
                db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e", Title = "Sixième" });
                db.BlobMediaAssets.Add(new BlobMediaAsset { Id = Guid.NewGuid(), SchoolId = schule, OwnerId = admin, KlasseId = klasse, BlobName = "a.png", OriginalFileName = "a.png", DisplayName = "Carte", ContentType = "image/png", Length = 10 });
                db.MailNachrichten.Add(new MailNachricht { Id = Guid.NewGuid(), IstEingehend = true, Absender = "parent@sql.test", Empfaenger = "contact@sql.test", Betreff = "Question", Inhalt = "<p>Texte</p>" });
                db.ExceptionLogs.Add(new ExceptionLog { Message = "Timeout", ExceptionType = "SqlException" });
                db.Ankuendigungen.Add(new Ankuendigung { Titel = "Info", InhaltHtml = "<p>Texte</p>", AblaufAmUtc = DateTime.UtcNow.AddDays(1) });
                db.KursNiveauDurchlaeufe.Add(new KursNiveauDurchlauf { Id = Guid.NewGuid(), SchulID = schule, KlasseId = klasse, StartDatum = DateTime.UtcNow, PlattformPreisProSchueler = 1000m, NiveauPreis = 5000m, ErstelltVonUserID = direktor });
                await db.SaveChangesAsync();
            }

            await using (var db = factory.CreateDbContext())
            {
                var users = await db.Users.ToListAsync();
                var manager = TestUserManager.Create(users);
                manager.SeedRole(users.Single(u => u.Id == admin), Rollen.SuperAdmin);
                var dashboard = new DashboardService(db, manager, new FakeMediaStorageService());

                var (nutzer, mehr) = await dashboard.SuchePlatformUsersAsync(admin, "Dir", Rollen.SchulLeiter, schule, 0, 10);
                Assert.Equal(direktor, Assert.Single(nutzer).UserId);
                Assert.False(mehr);
                var anzahl = await dashboard.ZaehlePlatformUsersNachRolleAsync(admin, "Enseignant", null);
                Assert.Equal(1, anzahl[Rollen.Lehrer]);
                Assert.Equal(1, anzahl[string.Empty]);
                Assert.Single((await dashboard.SucheMediaLibraryAsync(admin, schule, "Carte", 0)).Eintraege);
                Assert.Equal((1, 10L), await dashboard.ZaehleMediaLibraryAsync(admin, schule, "6e"));
                Assert.Single(await dashboard.GetMediaLibrarySchulenAsync(admin));

                Assert.Single((await new ZyklusTeilnahmeService(db).SucheZyklenAsync(admin, "SQL", true, 0)).Eintraege);
                Assert.Single((await new AnkuendigungService(db).SucheAsync("Info", 0)).Eintraege);
                Assert.Single(await new FinanzService(db).GetDurchlaeufeAsync(schule));
                Assert.NotNull(await new FinanzService(db).GetDirektorKontostandUebersichtAsync(schule));
                Assert.Empty((await new FinanzService(db).GetUebersichtAsync(new FinanzFilter { SchulID = schule, Von = DateTime.Today.AddYears(-1) })).Zeilen);
                Assert.Empty(await new TeacherLessonService(db).GetLessonsForTeacherOrSchoolDirectionAsync(direktor, true));
                Assert.Single((await ExceptionLogSuche.SucheAsync(db, new ExceptionLogFilter(Suche: "Sql"), 0)).Eintraege);
            }

            var mailbox = new MailboxService(factory, new FakeEmailService());
            Assert.Single((await mailbox.SucheAsync(admin, null, true, "Texte", 0)).Eintraege);
            var bulletins = new BulletinService(factory, new BewertungsService(factory));
            Assert.Empty((await bulletins.SucheListeAsync(direktor, schule, new BulletinListenFilter(Guid.NewGuid(), Guid.NewGuid(), klasse, "Ama"), 0)).Eintraege);
        }
        finally
        {
            // Niemals eine konfigurierbare Produktiv-/Entwicklungsdatenbank löschen.
            if (!name.StartsWith("SKOULOUAPP_TEST_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database name.");
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    private sealed class SqlFactory(string name) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"Server=localhost;Database={name};Integrated Security=True;TrustServerCertificate=True;Encrypt=True", sql => sql.EnableRetryOnFailure()).Options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
