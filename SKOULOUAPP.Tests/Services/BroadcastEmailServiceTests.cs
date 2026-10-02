using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Services.Email;
using SkoulouApp.Tests.Infrastructure;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für <see cref="BroadcastEmailService"/> – insbesondere für die manuell
/// eingegebenen Empfängeradressen. Der Versand läuft über
/// <see cref="FakeEmailService"/>, es wird keine echte E-Mail verschickt.
/// </summary>
public class BroadcastEmailServiceTests
{
    private static readonly EmailMessage Nachricht = new() { Subject = "Info", HtmlBody = "<p>Bonjour</p>" };

    /// <summary>Legt eine Schule mit je einem Schüler und einem Lehrer an.</summary>
    private static async Task<(TestDbContext Db, Guid SchoolId)> SeedAsync()
    {
        var db = TestDbContext.CreateInMemory();

        var school = new School { Id = Guid.NewGuid(), Name = "École Test" };
        db.Schools.Add(school);

        var schuelerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Schueler, NormalizedName = "SCHÜLER" };
        var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer, NormalizedName = "LEHRER" };
        db.Roles.AddRange(schuelerRolle, lehrerRolle);

        var schueler = new AppUser { Id = Guid.NewGuid(), UserName = "eleve", Email = "eleve@test.local", Vorname = "Ana", Nachname = "Kodjo" };
        var lehrer = new AppUser { Id = Guid.NewGuid(), UserName = "prof", Email = "prof@test.local", Vorname = "Marc", Nachname = "Adjo" };
        db.Users.AddRange(schueler, lehrer);

        db.UserRoles.AddRange(
            new AppUserRole { UserId = schueler.Id, RoleId = schuelerRolle.Id },
            new AppUserRole { UserId = lehrer.Id, RoleId = lehrerRolle.Id });

        db.UserSchools.AddRange(
            new UserSchool { AppUserId = schueler.Id, SchoolId = school.Id },
            new UserSchool { AppUserId = lehrer.Id, SchoolId = school.Id });

        await db.SaveChangesAsync();
        return (db, school.Id);
    }

    [Fact]
    public async Task ManualAddresses_SendetNurAnDieEingegebenenAdressen()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var result = await service.SendBroadcastAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.ManualAddresses,
            ZusaetzlicheAdressen = ["extern@example.com"]
        }, Nachricht);

        Assert.Equal(1, result.SentCount);
        var empfaenger = Assert.Single(email.GesendeteBatches).Recipients;
        Assert.Equal("extern@example.com", Assert.Single(empfaenger).Email);
    }

    [Fact]
    public async Task ManualAddresses_AkzeptiertNichtRegistrierteAdressen()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var recipients = await service.ResolveRecipientsAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.ManualAddresses,
            ZusaetzlicheAdressen = ["niemand@fremd.tld"]
        });

        Assert.Single(recipients);
    }

    [Fact]
    public async Task ManualAddresses_IgnoriertUngueltigeEingaben()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var recipients = await service.ResolveRecipientsAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.ManualAddresses,
            ZusaetzlicheAdressen = ["gueltig@example.com", "kaputt", "auch-kaputt@", "   "]
        });

        Assert.Equal("gueltig@example.com", Assert.Single(recipients).Email);
    }

    [Fact]
    public async Task ManualAddresses_OhneGueltigeAdresse_MeldetFehler()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var result = await service.SendBroadcastAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.ManualAddresses,
            ZusaetzlicheAdressen = ["kaputt"]
        }, Nachricht);

        Assert.Equal(0, result.SentCount);
        Assert.Contains(result.Errors, e => e.Contains("valide"));
        Assert.Empty(email.GesendeteBatches);
    }

    [Fact]
    public async Task Rollenauswahl_NimmtZusaetzlicheAdressenMitAuf()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var recipients = await service.ResolveRecipientsAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.Students,
            ZusaetzlicheAdressen = ["extern@example.com"]
        });

        Assert.Equal(2, recipients.Count);
        Assert.Contains(recipients, r => r.Email == "eleve@test.local");
        Assert.Contains(recipients, r => r.Email == "extern@example.com");
    }

    [Fact]
    public async Task ZusaetzlicheAdresse_DoppeltZuNutzer_WirdNurEinmalVersendet()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var recipients = await service.ResolveRecipientsAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.Students,
            ZusaetzlicheAdressen = ["ELEVE@test.local"]
        });

        Assert.Single(recipients);
    }

    [Fact]
    public async Task OhneZusaetzlicheAdressen_BleibtRollenauswahlUnveraendert()
    {
        var (db, _) = await SeedAsync();
        using var _db = db;
        var email = new FakeEmailService();
        var service = new BroadcastEmailService(db, email);

        var recipients = await service.ResolveRecipientsAsync(new BroadcastCriteria
        {
            Audience = BroadcastAudience.Teachers
        });

        Assert.Equal("prof@test.local", Assert.Single(recipients).Email);
    }
}
