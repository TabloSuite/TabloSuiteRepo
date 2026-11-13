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

    // ------------------------------------------------ Beim App-Start (wie SKOULOU)

    private static async Task<bool> StartSeedAsync(ServiceProvider sp, IConfiguration konfiguration)
    {
        using var scope = sp.CreateScope();
        return await ApplicationDbInitializer.SeedSuperAdminBeimStartAsync(
            scope.ServiceProvider.GetRequiredService<RoleManager<Role>>(),
            scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),
            konfiguration);
    }

    [Fact]
    public async Task Beim_Start_wird_ein_fehlender_Admin_aus_der_App_Konfiguration_angelegt()
    {
        await using var sp = Dienste();
        var konfiguration = Konfiguration(
            ("Seed:SuperAdminEmail", "admin@example.test"),
            ("Seed:SuperAdminPasswort", TestPasswort),
            ("Seed:SuperAdminBenutzername", "AdminTest"));

        Assert.True(await StartSeedAsync(sp, konfiguration));
        Assert.False(await StartSeedAsync(sp, konfiguration));   // zweiter Start: nichts mehr zu tun

        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = Assert.Single(userManager.Users);
        Assert.True(await userManager.IsInRoleAsync(admin, AppRollen.SuperAdmin));
        Assert.True(await userManager.CheckPasswordAsync(admin, TestPasswort));
    }

    [Fact]
    public async Task Beim_Start_bleibt_ein_vorhandenes_Konto_unveraendert()
    {
        await using var sp = Dienste();
        await StartSeedAsync(sp, Konfiguration(
            ("Seed:SuperAdminEmail", "admin@example.test"),
            ("Seed:SuperAdminPasswort", TestPasswort),
            ("Seed:SuperAdminBenutzername", "AdminTest")));

        // Andere Werte in der Konfiguration ändern nichts am bestehenden Konto (kein Passwort-Reset)
        Assert.False(await StartSeedAsync(sp, Konfiguration(
            ("Seed:SuperAdminEmail", "andere@example.test"),
            ("Seed:SuperAdminPasswort", "Anderes-Passwort-9!"),
            ("Seed:SuperAdminBenutzername", "AdminTest"))));

        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = Assert.Single(userManager.Users);
        Assert.Equal("admin@example.test", admin.Email);
        Assert.True(await userManager.CheckPasswordAsync(admin, TestPasswort));
    }

    [Fact]
    public async Task Beim_Start_ohne_vollstaendige_Konfiguration_passiert_nichts()
    {
        await using var sp = Dienste();

        Assert.False(await StartSeedAsync(sp, Konfiguration(("Seed:SuperAdminEmail", "admin@example.test"), ("Seed:SuperAdminPasswort", TestPasswort))));

        using var scope = sp.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().Users);
    }
}
