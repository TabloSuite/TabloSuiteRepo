using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ENote.Tests;

/// <summary>PIN vergessen: nur der Besitzer selbst entsperrt – mit dem Passwort seines Kontos.</summary>
public class BesitzerPinHilfeTests
{
    private const string Passwort = "Geheim-123";

    private static async Task<(ServiceProvider Sp, Guid Besitzer, Guid Kassierer, Guid SalonId)> BaueAsync()
    {
        var factory = TestDbContextFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<BesitzerPinHilfe>();
        var sp = services.BuildServiceProvider();

        var salon = new Salon { Name = "Salon PIN" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            await db.SaveChangesAsync();
        }

        using var scope = sp.CreateScope();
        var rollen = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        await rollen.CreateAsync(new Role { Name = AppRollen.Besitzer });
        await rollen.CreateAsync(new Role { Name = AppRollen.Kassierer });
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var besitzer = new AppUser { UserName = "besitzer", FullName = "Besitzer" };
        var kassierer = new AppUser { UserName = "kassierer", FullName = "Kassierer" };
        Assert.True((await users.CreateAsync(besitzer, Passwort)).Succeeded);
        Assert.True((await users.CreateAsync(kassierer, Passwort)).Succeeded);
        await users.AddToRoleAsync(besitzer, AppRollen.Besitzer);
        await users.AddToRoleAsync(kassierer, AppRollen.Kassierer);

        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id });
            db.UserSalons.Add(new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id });
            await db.SaveChangesAsync();
        }
        return (sp, besitzer.Id, kassierer.Id, salon.Id);
    }

    [Fact]
    public async Task Besitzer_entsperrt_nur_mit_richtigem_Passwort()
    {
        var (sp, besitzer, _, salonId) = await BaueAsync();
        using var scope = sp.CreateScope();
        var hilfe = scope.ServiceProvider.GetRequiredService<BesitzerPinHilfe>();

        Assert.False((await hilfe.PruefePasswortAsync(besitzer, salonId, "falsch")).Erfolg);
        Assert.False((await hilfe.PruefePasswortAsync(besitzer, salonId, "")).Erfolg);
        Assert.True((await hilfe.PruefePasswortAsync(besitzer, salonId, Passwort)).Erfolg);
    }

    [Fact]
    public async Task Kassierer_oder_fremder_Laden_kann_nicht_entsperren()
    {
        var (sp, besitzer, kassierer, _) = await BaueAsync();
        using var scope = sp.CreateScope();
        var hilfe = scope.ServiceProvider.GetRequiredService<BesitzerPinHilfe>();

        Assert.False((await hilfe.PruefePasswortAsync(kassierer, Guid.NewGuid(), Passwort)).Erfolg);
        Assert.False((await hilfe.PruefePasswortAsync(besitzer, Guid.NewGuid(), Passwort)).Erfolg);
    }

    [Fact]
    public void Willkommensmails_erklaeren_die_PIN_ohne_sie_zu_verschicken()
    {
        var validiert = TabloSuiteMails.InscriptionValidee("Ama", "eNOTE", "https://enote.tablosuite.com");
        var erstellt = TabloSuiteMails.KontoErstellt("Ama", "ama", "Salon", "https://enote.tablosuite.com", null);

        Assert.Contains("code PIN", validiert);
        Assert.Contains("code PIN", erstellt);
        Assert.Contains("jamais par e-mail", EmailService.AlsText(erstellt));
    }
}
