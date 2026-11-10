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
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Vorname = "Jean",
            Nachname = "Dupont",
            FullName = "Jean Dupont"
        };
        db.Users.Add(user);

        if (!string.IsNullOrWhiteSpace(rolle))
        {
            var role = new Role { Id = Guid.NewGuid(), Name = rolle, NormalizedName = rolle.ToUpperInvariant() };
            db.Roles.Add(role);
            db.UserRoles.Add(new AppUserRole { UserId = user.Id, RoleId = role.Id });
        }

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

        await service.SendWelcomeEmailAsync("keine-email");

        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task VersandFehler_LaesstRegistrierungUnberuehrt()
    {
        var email = new FakeEmailService { FehlerMeldung = "Inactive recipient" };
        var service = ErstelleService(email, out _, out _);

        // Darf keine Ausnahme werfen.
        await service.SendWelcomeEmailAsync("jean@test.local");

        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task VersandAusnahme_WirdGeschluckt()
    {
        var email = new FakeEmailService { WirftAusnahme = new HttpRequestException("Netzwerkfehler") };
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "jean@test.local", string.Empty);

        await service.SendWelcomeEmailAsync("jean@test.local");

        Assert.Empty(email.Gesendet);
    }

    [Fact]
    public async Task OhneKontext_SendetNeutraleWillkommensmail()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "unbekannt@test.local", string.Empty);

        await service.SendWelcomeEmailAsync("unbekannt@test.local");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Equal("Bienvenue chez SKOULOUAPP", nachricht.Subject);
        Assert.Contains("Jean", nachricht.HtmlBody);
    }

    [Fact]
    public async Task JedeMail_EnthaeltKlartextTeilGegenSpam()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "jean@test.local", string.Empty);

        await service.SendWelcomeEmailAsync("jean@test.local");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.False(string.IsNullOrWhiteSpace(nachricht.TextBody));
        Assert.DoesNotContain("<", nachricht.TextBody);
    }

    [Fact]
    public async Task JedeMail_NutztDasGemeinsameTemplateMitHeaderUndFooter()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "jean@test.local", string.Empty);

        await service.SendWelcomeEmailAsync("jean@test.local");

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
            ? await service.ResetPasswordAsync(user.Id, "secret-token", "Secret123!")
            : await service.ChangePasswordAsync(user.Id, "old-secret", "Secret123!");

        Assert.True(result.Succeeded);
        var sent = Assert.Single(email.Gesendet);
        Assert.Equal(user.Email, sent.Recipient.Email);
        Assert.Contains(wording, sent.Message.HtmlBody);
        Assert.False(string.IsNullOrWhiteSpace(sent.Message.TextBody));
        Assert.DoesNotContain("Secret123!", sent.Message.HtmlBody);
        Assert.DoesNotContain("secret-token", sent.Message.HtmlBody);
        Assert.Contains("https://localhost:7064/auth/login", sent.Message.HtmlBody);
        Assert.Contains("https://localhost:7064/auth/forgot-password", sent.Message.HtmlBody);
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
            ? await service.ResetPasswordAsync(user.Id, "invalid", "Secret123!")
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

        await service.SendWelcomeEmailAsync("admin@test.local");

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

        await service.SendWelcomeEmailAsync("chef@test.local");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Contains("école SKOULOUAPP est enregistrée", nachricht.Subject);
        Assert.Contains("Institut Goethe", nachricht.HtmlBody);
        Assert.Contains("directeur d'école", nachricht.HtmlBody);
        Assert.Contains("href=\"https://test.local/auth/login\"", nachricht.HtmlBody);
        Assert.DoesNotContain("href=\"https://test.local/login\"", nachricht.HtmlBody);
        Assert.Contains("Tout ton établissement, dans un seul espace", nachricht.HtmlBody);
        Assert.Contains("Frais scolaires", nachricht.HtmlBody);
        Assert.Contains("paiements comptants ou échelonnés", nachricht.HtmlBody);
        Assert.Contains("Finances de l'école", nachricht.HtmlBody);
        Assert.Contains("recettes", nachricht.HtmlBody);
        Assert.Contains("dépenses", nachricht.HtmlBody);
        Assert.Contains("SKOULOUAPPABO", nachricht.HtmlBody);
        Assert.Contains("commissions", nachricht.HtmlBody);
        Assert.Contains("montants facturés", nachricht.HtmlBody);
    }

    [Fact]
    public async Task PasswordResetMail_VerwendetDieVorhandeneResetRoute()
    {
        var email = new FakeEmailService();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "jean@test.local",
            UserName = "jean@test.local",
            Vorname = "Jean",
            FullName = "Jean Dupont"
        };
        var manager = TestUserManager.Create([user]);
        manager.PasswordResetToken = "jeton avec espaces";
        var factory = new TestApplicationDbContextFactory();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = "https://test.local/" })
            .Build();
        var service = new AuthService(factory.CreateDbContext(), factory, manager, null!, email,
            Options.Create(new PostmarkOptions()), configuration, NullLogger<AuthService>.Instance);
        await using (var db = factory.CreateDbContext())
        {
            db.Users.Add(new AppUser { Id = user.Id, UserName = user.UserName, NormalizedUserName = user.UserName.ToUpperInvariant(),
                Email = user.Email, NormalizedEmail = user.Email.ToUpperInvariant(), Vorname = "Jean", FullName = "Jean Dupont" });
            await db.SaveChangesAsync();
        }

        await service.SendPasswordResetAsync(user.Email);

        var html = Assert.Single(email.Gesendet).Message.HtmlBody;
        Assert.Contains($"https://test.local/auth/reset-password?id={user.Id}&amp;token=jeton%20avec%20espaces", html);
        Assert.Contains("jean@test.local", html);
        Assert.DoesNotContain("https://test.local/reset-password", html);
    }

    [Fact]
    public async Task Lehrer_ErhaeltLehrerMail()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "prof@test.local", Rollen.Lehrer, "École Test");

        await service.SendWelcomeEmailAsync("prof@test.local");

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

        await service.SendWelcomeEmailAsync("eleve@test.local");

        var html = Assert.Single(email.Gesendet).Message.HtmlBody;
        Assert.Contains("SKOULOUAPPABO", html);
        Assert.Contains("tu bénéficies des leçons, supports, exercices et examens", html);
        Assert.Contains("La direction attribue et suit cet abonnement pour chaque classe", html);
        Assert.Contains("Ton espace scolaire, toujours à portée de main", html);
        Assert.Contains("Cours et supports", html);
        Assert.Contains("Exercices et examens", html);
        Assert.Contains("Progression personnelle", html);
        Assert.Contains("direction de ton école", html);
    }

    [Fact]
    public async Task SchuelerWeitererSchule_ErhaeltDenEinheitlichenKlassenaboHinweis()
    {
        var email = new FakeEmailService();
        var service = ErstelleService(email, out _, out var factory);
        await SeedAsync(factory, "eleve2@test.local", Rollen.Schueler, "École Test");

        await service.SendWelcomeEmailAsync("eleve2@test.local");

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

        await service.SendWelcomeEmailAsync("chef2@test.local");

        var nachricht = Assert.Single(email.Gesendet).Message;
        Assert.Contains("école SKOULOUAPP est enregistrée", nachricht.Subject);
    }
}
