using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ENote.WebApp.Data.Seed;

public static class ApplicationDbInitializer
{
    public static async Task SeedAsync(
        ApplicationDbContext dbContext,
        RoleManager<Role> roleManager,
        UserManager<AppUser> userManager,
        IConfiguration configuration)
    {
        await SeedRollenAsync(roleManager);
        await SeedSuperAdminAsync(userManager, configuration);
        await SeedPlattformPreiseAsync(dbContext);
    }

    private static async Task SeedRollenAsync(RoleManager<Role> roleManager)
    {
        var beschreibungen = new Dictionary<string, string>
        {
            [AppRollen.SuperAdmin] = "Globale Verwaltung der Plattform.",
            [AppRollen.ProduktOwner] = "Zugewiesene Salons und Plattformfunktionen.",
            [AppRollen.Besitzer] = "Verwaltet den eigenen Salon, Leistungen, Rabatte und Kassierer.",
            [AppRollen.Kassierer] = "Startet die Kasse per PIN und kassiert."
        };

        foreach (var rollenName in AppRollen.Alle)
        {
            var vorhanden = await roleManager.FindByNameAsync(rollenName);
            var beschreibung = beschreibungen[rollenName];

            if (vorhanden is null)
            {
                PruefeErgebnis(await roleManager.CreateAsync(new Role
                {
                    Name = rollenName,
                    NormalizedName = rollenName.ToUpperInvariant(),
                    Description = beschreibung
                }));
            }
            else if (vorhanden.Description != beschreibung)
            {
                vorhanden.Description = beschreibung;
                vorhanden.UpdatedAtUtc = DateTime.UtcNow;
                PruefeErgebnis(await roleManager.UpdateAsync(vorhanden));
            }
        }
    }

    private static async Task SeedSuperAdminAsync(UserManager<AppUser> userManager, IConfiguration configuration)
    {
        // Einziger Seed-Benutzer. Zugangsdaten kommen aus Konfiguration/User Secrets,
        // niemals aus dem Repository. Ohne Konfiguration wird kein Admin angelegt.
        var email = configuration["Seed:SuperAdminEmail"];
        var passwort = configuration["Seed:SuperAdminPasswort"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(passwort))
        {
            return;
        }

        var benutzername = configuration["Seed:SuperAdminBenutzername"] ?? email;
        var vorhanden = await userManager.FindByNameAsync(benutzername);
        if (vorhanden is not null)
        {
            if (!await userManager.IsInRoleAsync(vorhanden, AppRollen.SuperAdmin))
                throw new InvalidOperationException("Das konfigurierte Seed-Konto existiert ohne SuperAdmin-Rolle. Keine automatische Rechteerweiterung.");
            return;
        }

        var admin = new AppUser
        {
            UserName = benutzername,
            Email = email,
            Vorname = "Super",
            Nachname = "Admin",
            FullName = "Super Admin",
            EmailConfirmed = true
        };

        var ergebnis = await userManager.CreateAsync(admin, passwort);
        PruefeErgebnis(ergebnis);
        PruefeErgebnis(await userManager.AddToRoleAsync(admin, AppRollen.SuperAdmin));
    }

    private static void PruefeErgebnis(IdentityResult ergebnis)
    {
        if (!ergebnis.Succeeded)
            throw new InvalidOperationException("Seed fehlgeschlagen: " + string.Join(", ", ergebnis.Errors.Select(e => e.Code)));
    }

    private static async Task SeedPlattformPreiseAsync(ApplicationDbContext dbContext)
    {
        // Sichert, dass es je Intervall mindestens einen aktuellen Standardpreis gibt.
        var standardPreise = new Dictionary<AboIntervall, decimal>
        {
            [AboIntervall.Monatlich] = 5000m,
            [AboIntervall.DreiMonate] = 13500m,
            [AboIntervall.Jaehrlich] = 50000m
        };

        foreach (var (intervall, preis) in standardPreise)
        {
            var vorhanden = await dbContext.PlattformPreise
                .AnyAsync(p => p.Intervall == intervall && p.IstAktuell);
            if (!vorhanden)
            {
                dbContext.PlattformPreise.Add(new PlattformPreis
                {
                    Intervall = intervall,
                    Preis = preis,
                    IstAktuell = true
                });
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
