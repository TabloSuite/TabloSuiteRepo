using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.Services;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using ENote.WebApp.Services.Sicherheit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Schutz der öffentlichen Formulare (15.11.2026, wie in SKOULOU): Adresse des Besuchers auch hinter dem Azure-Eingangsserver,
/// Bremse für Registrierung und « Mot de passe oublié », Rate-Limit der Anmeldung, Sperre fällt beim Zurücksetzen per Link.
/// </summary>
public class SchutzgrenzenTests
{
    private sealed class Uhr(DateTimeOffset jetzt) : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => Jetzt;
    }

    [Theory]
    // Azure, Weiterleitung noch nicht ausgewertet: der letzte Eintrag (vom Eingangsserver angehängt) ist der Besucher
    [InlineData("169.254.130.1", "203.0.113.5:51234", false, true, "203.0.113.5")]
    [InlineData("169.254.130.1", "10.9.9.9, 203.0.113.5:51234", false, true, "203.0.113.5")]
    [InlineData("169.254.130.1", "[2001:db8::7]:443", false, true, "2001:db8::7")]
    // Azure, vom Framework schon ausgewertet (X-Original-For): die Verbindung IST der Besucher
    [InlineData("203.0.113.5", "10.9.9.9", true, true, "203.0.113.5")]
    // nicht hinter Azure: eine mitgeschickte Kopfzeile wird ignoriert
    [InlineData("198.51.100.20", "203.0.113.5", false, false, "198.51.100.20")]
    [InlineData("169.254.130.1", "pas-une-adresse", false, true, "169.254.130.1")]
    [InlineData(null, null, false, false, null)]
    public void Anschluss_ist_die_Adresse_des_Besuchers_auch_hinter_dem_Azure_Eingangsserver(
        string? verbindung, string? weitergeleitet, bool schonAusgewertet, bool hinterAzure, string? erwartet)
        => Assert.Equal(erwartet, Anschluss.Adresse(verbindung, weitergeleitet, schonAusgewertet, hinterAzure));

    [Fact]
    public void Bremse_zaehlt_je_Schluessel_im_Zeitfenster()
    {
        var uhr = new Uhr(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));
        var bremse = new MissbrauchsBremse(uhr);
        var grenze = MissbrauchsBremse.OublieJeKonto; // 3 je Stunde
        for (var i = 0; i < grenze.Max; i++)
        {
            Assert.True(bremse.IstErlaubt("oublie-konto", "ama.mensah", grenze));
            bremse.Merken("oublie-konto", "ama.mensah");
        }
        Assert.False(bremse.IstErlaubt("oublie-konto", "ama.mensah", grenze));
        Assert.False(bremse.IstErlaubt("oublie-konto", " AMA.MENSAH ", grenze));   // Groß/klein und Leerzeichen zählen nicht
        Assert.True(bremse.IstErlaubt("oublie-konto", "kofi.agbo", grenze));        // anderes Konto
        Assert.True(bremse.IstErlaubt("inscription-adresse", "ama.mensah", grenze)); // anderer Bereich
        Assert.True(bremse.IstErlaubt("oublie-konto", null, grenze));               // Anschluss unbekannt: Grenze greift nicht
        uhr.Jetzt += TimeSpan.FromMinutes(61);
        Assert.True(bremse.IstErlaubt("oublie-konto", "ama.mensah", grenze));
    }

    [Fact]
    public void Grenzen_sind_grosszuegig_fuer_ein_Team_am_selben_Anschluss()
    {
        Assert.True(Schutzgrenzen.AnmeldungenJeMinute >= 50);
        Assert.True(MissbrauchsBremse.OublieJeAnschluss.Max >= 10);
        Assert.Contains("quelques minutes", Schutzgrenzen.MeldungLimite);
    }

    private static ServiceProvider Dienste(IDbContextFactory<ApplicationDbContext> factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Registrierung_ist_je_Adresse_und_je_Anschluss_gebremst()
    {
        var factory = TestDbContextFactory.Create();
        await using var sp = Dienste(factory);
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        foreach (var r in new[] { AppRollen.Besitzer, AppRollen.SuperAdmin })
            Assert.True((await rollen.CreateAsync(new Role { Name = r })).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MobileMoneyAnbieter.Add(new MobileMoneyAnbieter { Name = "Flooz", Land = "Togo", IstAktiv = true });
            await db.SaveChangesAsync();
        }
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);
        var auth = new AuthService(factory, sp.GetRequiredService<UserManager<AppUser>>(), null!, NullLogger<AuthService>.Instance, email,
            bremse: new MissbrauchsBremse());

        // Dieselbe E-Mail-Adresse (Besitzer mit mehreren Läden): 3 am Tag, dann Schluss – auch von einem anderen Anschluss.
        for (var i = 0; i < MissbrauchsBremse.InscriptionJeAdresse.Max; i++)
        {
            var ok = await auth.RegisterBesitzerAsync("Ama", "Mensah", $"ama.laden{i}", "ama@exemple.tg", "Init123!ab", $"Salon Ama {i}", "Lomé", land: "Togo", anschluss: "203.0.113.5", telefon: "+228 90 12 34 56");
            Assert.True(ok.Succeeded, ok.ErrorMessage);
        }
        var zuViel = await auth.RegisterBesitzerAsync("Ama", "Mensah", "ama.laden9", "AMA@exemple.tg", "Init123!ab", "Salon Ama 9", "Lomé", land: "Togo", anschluss: "198.51.100.9", telefon: "+228 90 12 34 56");
        Assert.False(zuViel.Succeeded);
        Assert.Equal(MissbrauchsBremse.MeldungInscription, zuViel.ErrorMessage);

        // Derselbe Anschluss mit immer neuen Adressen: 10 am Tag (3 sind schon gezählt), dann Schluss.
        for (var i = 3; i < MissbrauchsBremse.InscriptionJeAnschluss.Max; i++)
        {
            var ok = await auth.RegisterBesitzerAsync("Kofi", "Agbo", $"kofi.laden{i}", $"kofi{i}@exemple.tg", "Init123!ab", $"Boutique Kofi {i}", "Lomé", land: "Togo", anschluss: "203.0.113.5", telefon: "+228 90 12 34 56");
            Assert.True(ok.Succeeded, ok.ErrorMessage);
        }
        var bot = await auth.RegisterBesitzerAsync("Yao", "Tété", "yao.bot", "yao@exemple.tg", "Init123!ab", "Boutique Yao", "Lomé", land: "Togo", anschluss: "203.0.113.5", telefon: "+228 90 12 34 56");
        Assert.Equal(MissbrauchsBremse.MeldungInscription, bot.ErrorMessage);
        // Anderer Anschluss, neue Adresse: weiter möglich. Eine abgelehnte Registrierung zählt nicht mit.
        var anderer = await auth.RegisterBesitzerAsync("Yao", "Tété", "yao.vrai", "yao@exemple.tg", "Init123!ab", "Boutique Yao", "Lomé", land: "Togo", anschluss: "198.51.100.9", telefon: "+228 90 12 34 56");
        Assert.True(anderer.Succeeded, anderer.ErrorMessage);
    }

    [Fact]
    public async Task Zuruecksetzen_per_Link_hebt_die_Sperre_nach_Fehlversuchen_auf()
    {
        var factory = TestDbContextFactory.Create();
        await using var sp = Dienste(factory);
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = "esi.tamakloe", Email = "esi@exemple.tg", Vorname = "Esi", Nachname = "Tamakloe" };
        Assert.True((await users.CreateAsync(user, "Init123!ab")).Succeeded);
        Assert.True((await sp.GetRequiredService<RoleManager<Role>>().CreateAsync(new Role { Name = AppRollen.Kassierer })).Succeeded);
        // 5 Fehlversuche: Konto gesperrt
        await users.SetLockoutEnabledAsync(user, true);
        for (var i = 0; i < 5; i++) await users.AccessFailedAsync(user);
        Assert.True(await users.IsLockedOutAsync(user));

        var emailService = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);
        var dienst = new KontoWiederherstellung(users, new ConfigurationBuilder().Build(), NullLogger<KontoWiederherstellung>.Instance, emailService);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Assert.True(await dienst.ReinitialiseAsync(user.Id, token, "Nouveau9!ab"));

        await using var db = await factory.CreateDbContextAsync();
        var danach = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Null(danach.LockoutEnd);
        Assert.Equal(0, danach.AccessFailedCount);
        Assert.False(await users.IsLockedOutAsync((await users.FindByIdAsync(user.Id.ToString()))!));
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(user.Id.ToString()))!, "Nouveau9!ab"));
    }
}
