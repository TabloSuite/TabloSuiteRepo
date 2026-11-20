using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
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
/// Klicktest 20.11.2026 (Registrierung « gezielt kaputt gemacht »): überlange Eingaben führten zu « HTTP 500 » und einem
/// Konto ohne Laden, Leerzeichen/Akzente im Pseudonym und Passwort-Regeln zu englischen Meldungen.
/// </summary>
public class RegistrierungEingabenTests
{
    [Theory]
    [InlineData("Kofi", "Mensah", "kofi.mensah", "kofi@exemple.tg", "Boutique Kofi", true)]
    [InlineData("Kofi", "Mensah", "kofi.mensah@exemple.tg", "kofi@exemple.tg", "Chez Fifi 💇🏾‍♀️ Coiffure & Spa", true)]
    [InlineData("Kofi", "Mensah", "Kofi Mensah", "kofi@exemple.tg", "Boutique Kofi", false)]        // Leerzeichen
    [InlineData("Kofi", "Mensah", "éliane.ngo", "kofi@exemple.tg", "Boutique Kofi", false)]         // Akzent
    [InlineData("Kofi", "Mensah", "kofi.mensah", "kofi@exemple.tg", "x", true)]
    [InlineData("   ", "Mensah", "kofi.mensah", "kofi@exemple.tg", "Boutique Kofi", false)]          // nur Leerzeichen
    public void Felder_werden_vor_dem_Anlegen_geprueft(string vorname, string nachname, string pseudonym, string email, string laden, bool ok)
        => Assert.Equal(ok, AuthService.PruefeRegistrierungsFelder(vorname, nachname, pseudonym, email, laden) is null);

    [Fact]
    public void Zu_lange_Felder_ergeben_franzoesische_Meldungen()
    {
        Assert.Contains("120 caractères", AuthService.PruefeRegistrierungsFelder("Kofi", "Mensah", "kofi", "k@exemple.tg", new string('L', 460)));
        Assert.Contains("256 caractères", AuthService.PruefeRegistrierungsFelder("Kofi", "Mensah", "kofi", new string('k', 300) + "@exemple.tg", "Boutique"));
        Assert.Contains("64 caractères", AuthService.PruefeRegistrierungsFelder("Kofi", "Mensah", "kofi." + new string('x', 295), "k@exemple.tg", "Boutique"));
        Assert.Contains("60 caractères", AuthService.PruefeRegistrierungsFelder(new string('A', 61), "Mensah", "kofi", "k@exemple.tg", "Boutique"));
    }

    private static async Task<(AuthService Auth, IDbContextFactory<ApplicationDbContext> Factory, ServiceProvider Sp)> AufbauAsync()
    {
        var factory = TestDbContextFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        // Gleiche Passwort-Regeln wie Program.cs, mit französischen Meldungen.
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequireDigit = true; o.Password.RequireLowercase = true; o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false; o.Password.RequiredLength = 6;
            })
            .AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>().AddErrorDescriber<FranzoesischeIdentityFehler>();
        var sp = services.BuildServiceProvider();
        foreach (var r in new[] { AppRollen.Besitzer, AppRollen.SuperAdmin })
            Assert.True((await sp.GetRequiredService<RoleManager<Role>>().CreateAsync(new Role { Name = r })).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MobileMoneyAnbieter.Add(new MobileMoneyAnbieter { Name = "Flooz", Land = "Togo", IstAktiv = true });
            await db.SaveChangesAsync();
        }
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);
        return (new AuthService(factory, sp.GetRequiredService<UserManager<AppUser>>(), null!, NullLogger<AuthService>.Instance, email), factory, sp);
    }

    [Fact]
    public async Task Ueberlanger_Ladenname_legt_kein_Konto_an()
    {
        var (auth, factory, sp) = await AufbauAsync();
        await using var _ = sp;
        var r = await auth.RegisterBesitzerAsync("Kofi", "Mensah", "kofi.lang", "kofi@exemple.tg", "kofi2026",
            "Boutique " + string.Concat(Enumerable.Repeat("Très-Longue-", 38)), "Lomé", land: "Togo", telefon: "+228 90 12 34 56");
        Assert.False(r.Succeeded);
        Assert.Contains("120 caractères", r.ErrorMessage);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Salons.CountAsync());
    }

    [Theory]
    [InlineData("KOFI2026", "minuscule")]
    [InlineData("kofimensah", "chiffre")]
    public async Task Passwort_Regeln_kommen_auf_Franzoesisch(string passwort, string erwartet)
    {
        var (auth, factory, sp) = await AufbauAsync();
        await using var _ = sp;
        var r = await auth.RegisterBesitzerAsync("Kofi", "Mensah", "kofi.mensah", "kofi@exemple.tg", passwort,
            "Boutique Kofi", "Lomé", land: "Togo", telefon: "+228 90 12 34 56");
        Assert.False(r.Succeeded);
        Assert.Contains(erwartet, r.ErrorMessage);
        Assert.DoesNotContain("Passwords must", r.ErrorMessage);
    }

    [Fact]
    public void Program_registriert_die_franzoesischen_Identity_Meldungen()
    {
        var program = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Quelle())!, "..", "eNOTE.WebApp", "Program.cs"));
        Assert.Contains("AddErrorDescriber<ENote.WebApp.Services.Sicherheit.FranzoesischeIdentityFehler>()", program);
    }

    private static string Quelle([System.Runtime.CompilerServices.CallerFilePath] string pfad = "") => pfad;
}
