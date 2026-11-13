using ENote.Core.DataModels.Identity;
using ENote.WebApp.Data;
using ENote.WebApp.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ENote.Tests;

/// <summary>Start-Admin nur aus Umgebungsvariablen der App – kein fester Benutzername im Code.</summary>
public class SuperAdminSeedTests
{
    private const string TestPasswort = "Test-Passwort-1!";

    private static ServiceProvider Dienste()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private static IConfiguration Konfiguration(params (string Schluessel, string Wert)[] werte)
        => new ConfigurationBuilder().AddInMemoryCollection(werte.Select(w => new KeyValuePair<string, string?>(w.Schluessel, w.Wert))).Build();

    private static async Task SeedAsync(ServiceProvider sp, IConfiguration konfiguration)
    {
        using var scope = sp.CreateScope();
        await ApplicationDbInitializer.SeedAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            konfiguration);
    }

    [Fact]
    public async Task Ohne_Konfiguration_wird_kein_Admin_angelegt()
    {
        await using var sp = Dienste();
        await SeedAsync(sp, Konfiguration());

        using var scope = sp.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().Users);
    }

    [Fact]
    public async Task Ohne_Benutzername_gibt_es_keinen_festen_Standardnamen()
    {
        await using var sp = Dienste();
        var konfiguration = Konfiguration(("Seed:SuperAdminEmail", "admin@example.test"), ("Seed:SuperAdminPasswort", TestPasswort));

        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(sp, konfiguration));

        using var scope = sp.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().Users);
    }

    [Fact]
    public async Task Mit_allen_Werten_wird_der_Admin_einmal_angelegt()
    {
        await using var sp = Dienste();
        var konfiguration = Konfiguration(
            ("Seed:SuperAdminEmail", "admin@example.test"),
            ("Seed:SuperAdminPasswort", TestPasswort),
            ("Seed:SuperAdminBenutzername", "AdminTest"));

        await SeedAsync(sp, konfiguration);
        await SeedAsync(sp, konfiguration);

        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = Assert.Single(userManager.Users);
        Assert.Equal("AdminTest", admin.UserName);
        Assert.True(await userManager.IsInRoleAsync(admin, AppRollen.SuperAdmin));
    }
}
