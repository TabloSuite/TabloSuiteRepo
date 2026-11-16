using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.Services;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Nutzer 16.11.2026: die Telefonnummer ist bei der Registrierung eines Besitzers Pflicht (öffentlich, durch den
/// Responsable und durch den Administrateur), damit man ihn leicht erreichen kann.
/// </summary>
public class TelefonPflichtTests
{
    [Theory]
    [InlineData("+228 90 12 34 56", "+228 90 12 34 56", null)]
    [InlineData("  90  12 34   56 ", "90 12 34 56", null)]            // Leerzeichen bereinigt
    [InlineData("(00228) 90-12.34.56", "(00228) 90-12.34.56", null)]
    [InlineData("", null, TelefonRegel.MeldungPflicht)]
    [InlineData("   ", null, TelefonRegel.MeldungPflicht)]
    [InlineData(null, null, TelefonRegel.MeldungPflicht)]
    [InlineData("90 12 34", null, TelefonRegel.MeldungKurz)]            // weniger als 8 Ziffern
    [InlineData("appelez-moi 90123456", null, TelefonRegel.MeldungUngueltig)]
    [InlineData("+228 90 12 34 56 78 90 12 34 56 78 90", null, TelefonRegel.MeldungUngueltig)]   // zu lang
    public void Regel_bereinigt_und_prueft(string? eingabe, string? nummer, string? fehler)
        => Assert.Equal((nummer, fehler), TelefonRegel.Pruefe(eingabe));

    [Fact]
    public async Task Oeffentliche_Registrierung_verlangt_das_Telefon_und_speichert_es_am_Konto()
    {
        var factory = TestDbContextFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var sp = services.BuildServiceProvider();
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        foreach (var r in new[] { AppRollen.Besitzer, AppRollen.SuperAdmin })
            Assert.True((await rollen.CreateAsync(new Role { Name = r })).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MobileMoneyAnbieter.Add(new MobileMoneyAnbieter { Name = "Flooz", Land = "Togo", IstAktiv = true });
            await db.SaveChangesAsync();
        }
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);
        var auth = new AuthService(factory, sp.GetRequiredService<UserManager<AppUser>>(), null!, NullLogger<AuthService>.Instance, email);

        var ohne = await auth.RegisterBesitzerAsync("Ama", "Mensah", "ama.sans", "ama@exemple.tg", "Init123!ab", "Salon Ama", "Lomé", land: "Togo");
        Assert.False(ohne.Succeeded);
        Assert.Equal(TelefonRegel.MeldungPflicht, ohne.ErrorMessage);
        var kurz = await auth.RegisterBesitzerAsync("Ama", "Mensah", "ama.court", "ama@exemple.tg", "Init123!ab", "Salon Ama", "Lomé", land: "Togo", telefon: "90 12");
        Assert.Equal(TelefonRegel.MeldungKurz, kurz.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(0, await db.Salons.CountAsync());   // ohne gültige Nummer wird nichts angelegt

        var mit = await auth.RegisterBesitzerAsync("Ama", "Mensah", "ama.ok", "ama@exemple.tg", "Init123!ab", "Salon Ama", "Lomé", land: "Togo", telefon: " +228  90 12 34 56 ");
        Assert.True(mit.Succeeded, mit.ErrorMessage);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal("+228 90 12 34 56", (await db.Users.SingleAsync(u => u.UserName == "ama.ok")).TelefonNummer);
    }

    [Fact]
    public void Alle_drei_Wege_haben_das_Pflichtfeld_im_Formular()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        var seiten = Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp", "Components", "Pages");
        Assert.Contains("@bind-Value=\"FormModel.Telefon\"", File.ReadAllText(Path.Combine(seiten, "Register.razor")));
        Assert.Contains("@bind=\"neuTelefon\"", File.ReadAllText(Path.Combine(seiten, "MeineLaeden.razor")));
        Assert.Contains("@bind=\"neuTelefon\"", File.ReadAllText(Path.Combine(seiten, "Admin.razor")));
        Assert.Contains("telefon: neuTelefon", File.ReadAllText(Path.Combine(seiten, "MeineLaeden.razor.cs")));
        Assert.Contains("telefon: neuTelefon", File.ReadAllText(Path.Combine(seiten, "Admin.razor.cs")));
    }
}
