using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.WebApp.Data;
using SkoulouApp.WebApp.Data.Seed;
using Xunit;

namespace SkoulouApp.Tests.Services;

/// <summary>Start-Admin nur aus der Konfiguration: ohne Angaben kein Konto, keine Rechteerweiterung bestehender Konten.</summary>
public class SuperAdminSeedTests
{
    private const string TestPasswort = "Test-Passwort-1!";

    private static ServiceProvider Dienste()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddIdentityCore<AppUser>(o =>
            {
                o.Password.RequireDigit = true;
                o.Password.RequiredLength = 8;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
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
    public async Task Ohne_Konfiguration_Wird_Kein_Konto_Angelegt()
    {
        await using var sp = Dienste();
        await SeedAsync(sp, Konfiguration());

        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Empty(userManager.Users);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<RoleManager<Role>>().FindByNameAsync(Rollen.SuperAdmin));
    }

    [Fact]
    public async Task Mit_Konfiguration_Wird_SuperAdmin_Einmal_Angelegt()
    {
        await using var sp = Dienste();
        var konfiguration = Konfiguration(("Seed:SuperAdminEmail", "admin@example.org"), ("Seed:SuperAdminPasswort", TestPasswort),
            ("Seed:SuperAdminIdentifiant", "admin.skoulou"));
        await SeedAsync(sp, konfiguration);
        await SeedAsync(sp, konfiguration);   // zweiter Start: nichts doppelt

        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = Assert.Single(userManager.Users.ToList());
        Assert.Equal("admin.skoulou", admin.UserName);
        Assert.Equal("admin@example.org", admin.Email);
        Assert.True(await userManager.IsInRoleAsync(admin, Rollen.SuperAdmin));
        Assert.True(await userManager.CheckPasswordAsync(admin, TestPasswort));
    }

    [Fact]
    public async Task Ohne_Identifiant_Gilt_Die_EMail_Als_Anmeldename()
    {
        await using var sp = Dienste();
        await SeedAsync(sp, Konfiguration(("Seed:SuperAdminEmail", "admin@example.org"), ("Seed:SuperAdminPasswort", TestPasswort)));

        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.NotNull(await userManager.FindByNameAsync("admin@example.org"));
    }

    [Fact]
    public async Task Bestehendes_Konto_Ohne_Rolle_Wird_Nicht_Erhoben()
    {
        await using var sp = Dienste();
        await SeedAsync(sp, Konfiguration());
        using (var scope = sp.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await userManager.CreateAsync(new AppUser { UserName = "admin.skoulou", FullName = "Élève" }, TestPasswort)).Succeeded);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(sp, Konfiguration(
            ("Seed:SuperAdminEmail", "admin@example.org"), ("Seed:SuperAdminPasswort", TestPasswort), ("Seed:SuperAdminIdentifiant", "admin.skoulou"))));

        using var pruefung = sp.CreateScope();
        var um = pruefung.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.False(await um.IsInRoleAsync((await um.FindByNameAsync("admin.skoulou"))!, Rollen.SuperAdmin));
    }
}
