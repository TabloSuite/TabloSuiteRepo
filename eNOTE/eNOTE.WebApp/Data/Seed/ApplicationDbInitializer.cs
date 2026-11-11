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

        // Login erfolgt über das Pseudonym. Standard ist "DodjiEtse", sofern kein
        // Benutzername konfiguriert ist; die Kontakt-E-Mail kommt aus der Konfiguration.
        var benutzername = configuration["Seed:SuperAdminBenutzername"] ?? "DodjiEtse";

        // 1) Konto mit Ziel-Pseudonym bereits vorhanden?
        var vorhanden = await userManager.FindByNameAsync(benutzername);
        if (vorhanden is not null)
        {
            if (!await userManager.IsInRoleAsync(vorhanden, AppRollen.SuperAdmin))
                throw new InvalidOperationException("Das konfigurierte Seed-Konto existiert ohne SuperAdmin-Rolle. Keine automatische Rechteerweiterung.");
            return;
        }

        // 2) Bestehenden SuperAdmin (z. B. alter Stand mit E-Mail als Benutzername) auf
        //    das Ziel-Pseudonym umstellen, damit der Login per Pseudonym funktioniert.
        // E-Mail ist nicht eindeutig: nur ein SuperAdmin mit dieser Adresse zählt.
        var normalisiert = userManager.NormalizeEmail(email);
        var kandidaten = userManager.Users.Where(u => u.NormalizedEmail == normalisiert).ToList();
        AppUser? bestehend = null;
        foreach (var k in kandidaten)
        {
            if (await userManager.IsInRoleAsync(k, AppRollen.SuperAdmin)) { bestehend = k; break; }
        }
        if (bestehend is null)
        {
            bestehend = (await userManager.GetUsersInRoleAsync(AppRollen.SuperAdmin)).FirstOrDefault();
        }

        if (bestehend is not null)
        {
            if (!await userManager.IsInRoleAsync(bestehend, AppRollen.SuperAdmin))
                throw new InvalidOperationException("Das gefundene Seed-Konto hat keine SuperAdmin-Rolle. Keine automatische Rechteerweiterung.");

            var namensAenderung = await userManager.SetUserNameAsync(bestehend, benutzername);
            PruefeErgebnis(namensAenderung);
            if (bestehend.Email != email)
            {
                PruefeErgebnis(await userManager.SetEmailAsync(bestehend, email));
            }
            return;
        }

        // 3) Kein Konto vorhanden → neu anlegen.
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
            // Monatspreis ist die Basis aller Formeln (Rabatte nach VertragsRegeln); die übrigen Werte passen dazu.
            [AboIntervall.Monatlich] = 3600m,
            [AboIntervall.DreiMonate] = 10260m,
            [AboIntervall.Jaehrlich] = 38880m
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
