using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Options;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>
/// Tests für die rollenabhängige Willkommensmail in
/// <see cref="AuthService"/>. Der Versand läuft ausschließlich über
/// <see cref="FakeEmailService"/> – es wird keine echte E-Mail verschickt.
/// </summary>
public class WillkommensMailTests
{
    private static AuthService ErstelleService(
        FakeEmailService email,
        out ApplicationDbContext db,
        out IDbContextFactory<ApplicationDbContext> factory)
    {
        var f = new TestApplicationDbContextFactory();
        factory = f;
        db = f.CreateDbContext();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://test.local" })
            .Build();

        // Postmark ist bewusst deaktiviert; der FakeEmailService ersetzt den realen Versand.
        var postmark = Options.Create(new PostmarkOptions
        {
            Enabled = false,
            FromEmail = "noreply@test.local",
            SupportEmail = "support@test.local"
        });

        return new AuthService(
            db,
            f,
            TestUserManager.Create(),
            signInManager: null!,
            email,
            postmark,
            configuration,
            NullLogger<AuthService>.Instance);
    }

    /// <summary>Legt einen Benutzer mit Rolle und optionaler Schule in der Testdatenbank an.</summary>
    private static async Task SeedAsync(
        IDbContextFactory<ApplicationDbContext> factory,
        string email,
        string rolle,
        string? schulName = null)
    {
        await using var db = await factory.CreateDbContextAsync();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            Vorname = "Jean",
            Nachname = "Dupont",
            FullName = "Jean Dupont"
        };
        db.Users.Add(user);

        var role = new Role { Id = Guid.NewGuid(), Name = rolle, NormalizedName = rolle.ToUpperInvariant() };
        db.Roles.Add(role);
        db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = role.Id });

        if (!string.IsNullOrWhiteSpace(schulName))
        {
            var schule = new School
            {
                Id = Guid.NewGuid(),
                Name = schulName,
                City = "Lomé",
                Country = "Togo",
                IsActive = true
            };
            db.Schools.Add(schule);
            db.UserSchools.Add(new UserSchool { AppUserId = user.Id, SchoolId = schule.Id });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UngueltigeAdresse_WirdIgnoriertUndNichtsGesendet()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out _);

        await service.SendWelcomeEmailAsync("keine-email", "Jean");

        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task VersandFehler_LaesstRegistrierungUnberuehrt()
    {
        var email = new FakeEmailService { FehlerMeldung = "Inactive recipient" };
        var service = ErstelleService(email, out _, out _);

        // Darf keine Ausnahme werfen.
        await service.SendWelcomeEmailAsync("jean@test.local", "Jean");

        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task VersandAusnahme_WirdGeschluckt()
    {
        var email = new FakeEmailService { WirftAusnahme = new HttpRequestException("Netzwerkfehler") };
        var service = ErstelleService(email, out _, out _);

        await service.SendWelcomeEmailAsync("jean@test.local", "Jean");

        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task OhneKontext_SendetNeutraleWillkommensmail()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out _);

        await service.SendWelcomeEmailAsync("unbekannt@test.local", "Jean");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Equal("Bienvenue chez SKOULOUAPP", nachricht.Subject);
        Assert.Contains("Jean", nachricht.HtmlBody);
    }

    [Fact]
    public async Task JedeMail_EnthaeltKlartextTeilGegenSpam()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out _);

        await service.SendWelcomeEmailAsync("jean@test.local", "Jean");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.False(string.IsNullOrWhiteSpace(nachricht.TextBody));
        Assert.DoesNotContain("<", nachricht.TextBody);
    }

    [Fact]
    public async Task JedeMail_NutztDasGemeinsameTemplateMitHeaderUndFooter()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out _);

        await service.SendWelcomeEmailAsync("jean@test.local", "Jean");

        var html = Assert.Single(email.Gesendet).Message.HtmlBody;
        Assert.Contains("SKOULOUAPP", html);
        Assert.Contains("CONTACT", html);
        Assert.Contains("Tous droits réservés", html);
    }

    [Theory]
    [InlineData(false, "modifié")]
    [InlineData(true, "réinitialisé")]
    public async Task ErfolgreichePasswortaktion_SendetBestaetigungOhnePasswortOderToken(bool reset, string wording)
    {
        var email = new FakeEmailService();
        var user = new AppUser { Id = Guid.NewGuid(), Email = "jean@test.local", Vorname = "Jean", FullName = "Jean Dupont" };
        var manager = TestUserManager.Create([user]);
        var factory = new TestApplicationDbContextFactory();
        var service = new AuthService(factory.CreateDbContext(), factory, manager, null!, email,
            Options.Create(new PostmarkOptions()), new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);

        var result = reset
            ? await service.ResetPasswordAsync(user.Email, "secret-token", "Secret123!")
            : await service.ChangePasswordAsync(user.Id, "old-secret", "Secret123!");

        Assert.True(result.Succeeded);
        var sent = Assert.Single(email.Gesendet);
        Assert.Equal(user.Email, sent.Recipient.Email);
        Assert.Contains(wording, sent.Message.HtmlBody);
        Assert.False(string.IsNullOrWhiteSpace(sent.Message.TextBody));
        Assert.DoesNotContain("Secret123!", sent.Message.HtmlBody);
        Assert.DoesNotContain("secret-token", sent.Message.HtmlBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FehlgeschlagenePasswortaktion_SendetKeineMail(bool reset)
    {
        var email = new FakeEmailService();
        var user = new AppUser { Id = Guid.NewGuid(), Email = "jean@test.local" };
        var manager = TestUserManager.Create([user]);
        var failure = IdentityResult.Failed(new IdentityError { Description = "Mot de passe invalide." });
        if (reset) manager.ResetPasswordResult = failure;
        else manager.ChangePasswordResult = failure;
        var factory = new TestApplicationDbContextFactory();
        var service = new AuthService(factory.CreateDbContext(), factory, manager, null!, email,
            Options.Create(new PostmarkOptions()), new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);

        var result = reset
            ? await service.ResetPasswordAsync(user.Email, "invalid", "Secret123!")
            : await service.ChangePasswordAsync(user.Id, "invalid", "Secret123!");

        Assert.False(result.Succeeded);
        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task FehlerBeimPasswortMailversand_MachtErfolgreichenPasswortwechselNichtRueckgaengig()
    {
        var email = new FakeEmailService { WirftAusnahme = new HttpRequestException("Réseau indisponible") };
        var user = new AppUser { Id = Guid.NewGuid(), Email = "jean@test.local" };
        var factory = new TestApplicationDbContextFactory();
        var service = new AuthService(factory.CreateDbContext(), factory, TestUserManager.Create([user]), null!, email,
            Options.Create(new PostmarkOptions()), new ConfigurationBuilder().Build(), NullLogger<AuthService>.Instance);

        var result = await service.ChangePasswordAsync(user.Id, "old", "new");

        Assert.True(result.Succeeded);
        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task SuperAdmin_ErhaeltAdministratorMail()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "admin@test.local", Rollen.SuperAdmin);

        await service.SendWelcomeEmailAsync("admin@test.local", "Jean");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Contains("administrateur", nachricht.Subject);
        Assert.Contains("valider les comptes des directeurs", nachricht.HtmlBody);
    }

    [Fact]
    public async Task SchulLeiter_ErhaeltDirektorenMailMitSchulname()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "chef@test.local", Rollen.SchulLeiter, "Institut Goethe");

        await service.SendWelcomeEmailAsync("chef@test.local", "Jean");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Contains("école SKOULOUAPP est enregistrée", nachricht.Subject);
        Assert.Contains("Institut Goethe", nachricht.HtmlBody);
        Assert.Contains("directeur d'école", nachricht.HtmlBody);
    }

    [Fact]
    public async Task Lehrer_ErhaeltLehrerMail()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "prof@test.local", Rollen.Lehrer, "École Test");

        await service.SendWelcomeEmailAsync("prof@test.local", "Jean");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Contains("enseignant", nachricht.Subject);
        Assert.Contains("tes élèves bénéficient", nachricht.HtmlBody);
    }

    [Fact]
    public async Task SchuelerNormaleSchule_ErhaeltAktuellenKlassenaboHinweis()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "eleve@test.local", Rollen.Schueler, "École Test");

        await service.SendWelcomeEmailAsync("eleve@test.local", "Jean");

        var html = Assert.Single(email.Gesendet).Message.HtmlBody;
        Assert.Contains("SKOULOUAPPABO", html);
        Assert.Contains("tu bénéficies des leçons, supports, exercices et examens", html);
        Assert.Contains("La direction attribue et suit cet abonnement pour chaque classe", html);
    }

    [Fact]
    public async Task SchuelerSprachschule_ErhaeltEbenfallsDenEinheitlichenKlassenaboHinweis()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "eleve2@test.local", Rollen.Schueler, "École Test");

        await service.SendWelcomeEmailAsync("eleve2@test.local", "Jean");

        var html = Assert.Single(email.Gesendet).Message.HtmlBody;
        Assert.Contains("SKOULOUAPPABO", html);
        Assert.Contains("tu bénéficies des leçons, supports, exercices et examens", html);
        Assert.Contains("La direction attribue et suit cet abonnement pour chaque classe", html);
    }

    [Fact]
    public async Task SchulLeiterMitLehrerRolle_ErhaeltDirektorenVariante()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "chef2@test.local", Rollen.SchulLeiter, "École Test");

        // Zusätzlich die Lehrer-Rolle zuweisen – die höhere Rolle muss gewinnen.
        await using (var db = await factory.CreateDbContextAsync())
        {
            var user = await db.Users.FirstAsync(x => x.Email == "chef2@test.local");
            var lehrerRolle = new Role { Id = Guid.NewGuid(), Name = Rollen.Lehrer, NormalizedName = "LEHRER" };
            db.Roles.Add(lehrerRolle);
            db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = lehrerRolle.Id });
            await db.SaveChangesAsync();
        }

        await service.SendWelcomeEmailAsync("chef2@test.local", "Jean");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Contains("école SKOULOUAPP est enregistrée", nachricht.Subject);
    }
}
