using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Opt-in: SKOULOU_SQL_TESTS=1. Ausschließlich zufällig benannte lokale Testdatenbank.</summary>
public class UnterrichtSqlTests
{
    [Fact]
    [Trait("Category", "SqlIntegration")]
    public async Task SqlServer_ParalleleNotenUndFremdschluesselSindKonsistent()
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_SQL_TESTS") != "1") return;
        var name = "SKOULOUAPP_TEST_" + Guid.NewGuid().ToString("N");
        var factory = new SqlFactory(name);
        try
        {
            await using (var schema = factory.CreateDbContext()) await schema.Database.MigrateAsync();
            var schule = Guid.NewGuid(); var direktor = Guid.NewGuid(); var schueler = Guid.NewGuid();
            var klasse = Guid.NewGuid(); var jahr = Guid.NewGuid(); var periode = Guid.NewGuid();
            var fach = Guid.NewGuid(); var bewertung = Guid.NewGuid(); var komponente = Guid.NewGuid(); var zuordnung = Guid.NewGuid();
            await using (var db = factory.CreateDbContext())
            {
                db.Schools.Add(new School { Id = schule, Name = "SQL-Test" });
                db.Users.AddRange(new AppUser { Id = direktor, Email = "direction@sql.test", FullName = "Direction" },
                    new AppUser { Id = schueler, Email = "eleve@sql.test", FullName = "Élève" });
                var rolle = new Role { Id = Guid.NewGuid(), Name = Rollen.SchulLeiter };
                db.Roles.Add(rolle); db.UserRoles.Add(new AppUserRole { UserId = direktor, RoleId = rolle.Id });
                db.UserSchools.Add(new UserSchool { AppUserId = direktor, SchoolId = schule });
                db.Klassen.Add(new Klasse { Id = klasse, SchoolId = schule, Code = "6e" });
                db.Faecher.Add(new Fach { Id = fach, SchoolId = schule, Name = "Mathématiques" });
                db.Add(new Schuljahr { Id = jahr, SchulID = schule, Bezeichnung = "2026", Beginn = new(2026, 1, 1), Ende = new(2026, 12, 31) });
                db.Add(new BewertungsPeriode { Id = periode, SchulID = schule, SchuljahrID = jahr, Titel = "Trimestre", Art = BewertungsArt.Trimester, Reihenfolge = 1 });
                db.Add(new BewertungsBestandteil { Id = komponente, PeriodeID = periode, Bezeichnung = "Composition", MaximalPunkte = 20 });
                db.Add(new FachBewertung { Id = bewertung, SchuljahrID = jahr, KlasseID = klasse, FachID = fach });
                db.Add(new SchuelerJahresZuordnung { Id = zuordnung, SchulID = schule, SchuljahrID = jahr, KlasseID = klasse, SchuelerID = schueler });
                await db.SaveChangesAsync();
            }
            var noten = new BewertungsService(factory);
            await Task.WhenAll(noten.NoteSpeichernAsync(direktor, komponente, bewertung, zuordnung, 10, "A"),
                noten.NoteSpeichernAsync(direktor, komponente, bewertung, zuordnung, 15, "B"));
            await using (var db = factory.CreateDbContext())
            {
                var note = Assert.Single(await db.Set<FachNote>().ToListAsync());
                var historie = await db.Set<NotenAenderung>().ToListAsync();
                Assert.Equal(2, historie.Count);
                var erste = Assert.Single(historie, x => x.AltePunkte == null);
                var zweite = Assert.Single(historie, x => x.AltePunkte != null);
                Assert.Equal(erste.NeuePunkte, zweite.AltePunkte);
                Assert.Equal(zweite.NeuePunkte, note.Punkte);
            }
            var bulletins = new BulletinService(factory, noten);
            await bulletins.VorlageSpeichernAsync(direktor, new() { SchulID = schule, KlasseID = klasse, Art = BewertungsArt.Trimester, Name = "SQL" });
            async Task<bool> Publizieren()
            {
                try { await bulletins.VeroeffentlichenAsync(direktor, schule, periode, zuordnung, null, "SQL"); return true; }
                catch (InvalidOperationException) { return false; }
            }
            Assert.Single(await Task.WhenAll(Publizieren(), Publizieren()), x => x);
            var klassePruefung = await bulletins.KlassePruefenAsync(direktor, schule, periode, klasse);
            await bulletins.KlasseEndgueltigVeroeffentlichenAsync(direktor, schule, periode, klasse, klassePruefung.Hash, true);
            var berichtigung = await bulletins.BerichtigungStartenAsync(direktor, schule, periode, klasse, "SQL-Berichtigung");
            Guid noteID;
            await using (var db = factory.CreateDbContext()) noteID = (await db.Set<FachNote>().SingleAsync()).Id;
            await bulletins.BerichtigungsNoteAsync(direktor, berichtigung, noteID, 18, "Corrigé");
            var wirkung = await bulletins.BerichtigungPruefenAsync(direktor, berichtigung);
            Assert.Equal(1, await bulletins.BerichtigungFreigebenAsync(direktor, berichtigung, wirkung.Hash, true));
            await using (var db = factory.CreateDbContext())
            {
                Assert.Single(await db.Set<BulletinAusgabe>().Where(x => x.IstVeroeffentlicht).ToListAsync());
                Assert.Equal(3, await db.Set<BulletinAusgabe>().CountAsync());
                Assert.Equal(18, (await db.Set<FachNote>().SingleAsync()).Punkte);
                db.Users.Remove(await db.Users.SingleAsync(x => x.Id == schueler));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            var identitaetsEntwurf = await bulletins.BerichtigungStartenAsync(direktor, schule, periode, klasse, "Identité erronée");
            var identitaet = await bulletins.BerichtigungsStammdatenLadenAsync(direktor, identitaetsEntwurf, zuordnung);
            await bulletins.BerichtigungsStammdatenAsync(direktor, identitaetsEntwurf, identitaet with { Name = "Identité SQL corrigée", Geburtsort = "Lomé" });
            var identitaetsPruefung = await bulletins.BerichtigungPruefenAsync(direktor, identitaetsEntwurf);
            Assert.Equal(1, await bulletins.BerichtigungFreigebenAsync(direktor, identitaetsEntwurf, identitaetsPruefung.Hash, true));
            await using (var db = factory.CreateDbContext())
            {
                Assert.Equal(4, await db.Set<BulletinAusgabe>().CountAsync());
                var aktuelle = await db.Set<BulletinAusgabe>().SingleAsync(x => x.IstVeroeffentlicht);
                Assert.Equal("Identité SQL corrigée", (await bulletins.LesenAsync(direktor, aktuelle.Id))!.Schueler);
                Assert.Equal(18, (await db.Set<FachNote>().SingleAsync()).Punkte);
            }
            await using (var db = factory.CreateDbContext())
            {
                // Restrict schützt Noten mit Historie vor versehentlicher Einzel-Löschung.
                db.Remove(await db.Set<FachNote>().SingleAsync());
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            await using (var db = factory.CreateDbContext())
            {
                db.RemoveRange(await db.Set<NotenAenderung>().ToListAsync()); await db.SaveChangesAsync();
                db.RemoveRange(await db.Set<FachNote>().ToListAsync()); await db.SaveChangesAsync();
                Assert.Empty(await db.Set<FachNote>().ToListAsync());
            }
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
