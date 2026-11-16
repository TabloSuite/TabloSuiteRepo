using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
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
/// W2: Land bei der Registrierung (Ortszeit, Mobile Money), später änderbar durch Besitzer (Paramètres) und Admin
/// (Établissements). Läden ohne Land bleiben Togo.
/// </summary>
public class LandTests
{
    private static ServiceProvider Dienste(IDbContextFactory<ApplicationDbContext> factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        return services.BuildServiceProvider();
    }

    private static async Task<(IDbContextFactory<ApplicationDbContext> F, ServiceProvider Sp, AuthService Auth)> BasisAsync()
    {
        var factory = TestDbContextFactory.Create();
        var sp = Dienste(factory);
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        foreach (var r in new[] { AppRollen.Besitzer, AppRollen.SuperAdmin })
            Assert.True((await rollen.CreateAsync(new Role { Name = r })).Succeeded);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.MobileMoneyAnbieter.AddRange(
                new MobileMoneyAnbieter { Name = "Flooz", Land = "Togo", IstAktiv = true },
                new MobileMoneyAnbieter { Name = "MTN MoMo", Land = "Bénin", IstAktiv = true },
                new MobileMoneyAnbieter { Name = "Démo", Land = "Albanie", IstAktiv = true },
                new MobileMoneyAnbieter { Name = "Ancien", Land = "Andorre", IstAktiv = false });
            await db.SaveChangesAsync();
        }
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);
        var auth = new AuthService(factory, sp.GetRequiredService<UserManager<AppUser>>(), null!, NullLogger<AuthService>.Instance, email);
        return (factory, sp, auth);
    }

    [Fact]
    public async Task Auswahl_Zeigt_Laender_Mit_Ortszeit_Oder_Mobile_Money_Hauptmarkt_Zuerst()
    {
        var (f, sp, _) = await BasisAsync();
        await using var _sp = sp;
        await using var db = await f.CreateDbContextAsync();
        var laender = await LaenderAuswahl.GetAsync(db);
        Assert.Equal("Togo", laender[0]);
        Assert.Contains("Bénin", laender);
        Assert.Contains("Cameroun", laender);   // Ortszeit hinterlegt
        Assert.Contains("Albanie", laender);    // nur über einen aktiven Mobile-Money-Anbieter
        Assert.DoesNotContain("Andorre", laender); // Anbieter inaktiv, keine Ortszeit
        Assert.Equal("UTC+1", Zeitzonen.VersatzText("Bénin", DateTime.UtcNow));
        Assert.Equal("Togo", await LaenderAuswahl.PruefeAsync(db, " "));
        Assert.Equal("Bénin", await LaenderAuswahl.PruefeAsync(db, "bénin"));
        await Assert.ThrowsAsync<ArgumentException>(() => LaenderAuswahl.PruefeAsync(db, "Atlantide"));
    }

    [Fact]
    public async Task Registrierung_Speichert_Das_Land_Und_Lehnt_Unbekannte_Ab()
    {
        var (f, sp, auth) = await BasisAsync();
        await using var _sp = sp;

        var ok = await auth.RegisterBesitzerAsync("Afi", "Hounsou", "afi.cotonou", "afi@exemple.bj", "Init123!ab", "Salon Afi", "Cotonou", land: "Bénin", telefon: "+228 90 12 34 56");
        Assert.True(ok.Succeeded, ok.ErrorMessage);
        var falsch = await auth.RegisterBesitzerAsync("Yao", "Mensah", "yao.x", "yao@exemple.tg", "Init123!ab", "Salon Yao", null, land: "Atlantide", telefon: "+228 90 12 34 56");
        Assert.False(falsch.Succeeded);
        Assert.Equal("Choisissez un pays dans la liste.", falsch.ErrorMessage);
        var alt = await auth.RegisterBesitzerAsync("Kofi", "Agbo", "kofi.lome", "kofi@exemple.tg", "Init123!ab", "Salon Kofi", "Lomé", telefon: "+228 90 12 34 56");
        Assert.True(alt.Succeeded, alt.ErrorMessage);

        await using var db = await f.CreateDbContextAsync();
        Assert.Equal("Bénin", (await db.Salons.SingleAsync(s => s.Name == "Salon Afi")).Land);
        Assert.False(await db.Users.AnyAsync(u => u.UserName == "yao.x")); // kein Konto ohne gültiges Land
        var ohneLand = await db.Salons.SingleAsync(s => s.Name == "Salon Kofi");
        Assert.Null(ohneLand.Land);                                             // wie bisher: Togo als Rückfall
        Assert.Equal(TimeSpan.Zero, Zeitzonen.Versatz(Zeitzonen.Fuer(ohneLand.Land), DateTime.UtcNow));
        Assert.Equal(["Flooz"], await MobileMoneyService.ErlaubteAnbieterAbfrage(db, ohneLand.Id).Select(a => a.Name).ToListAsync());
    }

    [Fact]
    public async Task Besitzer_Und_Admin_Aendern_Das_Land_Mobile_Money_Folgt()
    {
        var (f, sp, _) = await BasisAsync();
        await using var _sp = sp;
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        async Task<AppUser> Konto(string name, string rolle)
        {
            var u = new AppUser { UserName = name, Email = name + "@exemple.tg", FullName = name };
            Assert.True((await users.CreateAsync(u, "Init123!ab")).Succeeded);
            Assert.True((await users.AddToRoleAsync(u, rolle)).Succeeded);
            return u;
        }
        var besitzer = await Konto("ama", AppRollen.Besitzer);
        var fremd = await Konto("fremd", AppRollen.Besitzer);
        var admin = await Konto("admin", AppRollen.SuperAdmin);
        var salon = new Salon { Name = "Salon Ama", IstAktiv = true, IstFreigegeben = true, MobileMoneyIndividuell = true };
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id });
            db.SalonMobileMoneyAnbieter.Add(new SalonMobileMoneyAnbieter { SalonId = salon.Id, AnbieterId = (await db.MobileMoneyAnbieter.SingleAsync(a => a.Name == "Flooz")).Id });
            await db.SaveChangesAsync();
        }

        var salons = new SalonService(f, null!, users);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => salons.SetzeLandAsync(fremd.Id, salon.Id, "Bénin"));
        await Assert.ThrowsAsync<ArgumentException>(() => salons.SetzeLandAsync(besitzer.Id, salon.Id, "Atlantide"));
        Assert.True(await salons.SetzeLandAsync(besitzer.Id, salon.Id, "Bénin"));
        await using (var db = await f.CreateDbContextAsync())
        {
            var s = await db.Salons.SingleAsync(x => x.Id == salon.Id);
            Assert.Equal(("Bénin", false), (s.Land, s.MobileMoneyIndividuell)); // Auswahl aus Togo gilt nicht mehr
            Assert.False(await db.SalonMobileMoneyAnbieter.AnyAsync());
            Assert.Equal(["MTN MoMo"], await MobileMoneyService.ErlaubteAnbieterAbfrage(db, salon.Id).Select(a => a.Name).ToListAsync());
            Assert.Equal(TimeSpan.FromHours(1), Zeitzonen.Versatz(Zeitzonen.Fuer(s.Land), DateTime.UtcNow));
        }

        var adminService = new AdminService(f, null!, NullLogger<AdminService>.Instance, null!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adminService.SetzeLandAsync(besitzer.Id, salon.Id, "Togo"));
        Assert.True(await adminService.SetzeLandAsync(admin.Id, salon.Id, "Togo"));
        Assert.False(await adminService.SetzeLandAsync(admin.Id, salon.Id, "Togo")); // unverändert
        await using (var db = await f.CreateDbContextAsync())
            Assert.Equal("Togo", (await db.Salons.SingleAsync(x => x.Id == salon.Id)).Land);
    }
}
