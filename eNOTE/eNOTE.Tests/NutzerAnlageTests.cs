using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ENote.Tests;

public class NutzerAnlageTests
{
    [Fact]
    public void Datenbankmodell_Erlaubt_Geteilte_Email_Aber_Nur_Einen_Login_Und_Laden()
    {
        using var db = TestDbContextFactory.Create().CreateDbContext();
        var konto = db.Model.FindEntityType(typeof(AppUser))!;
        Assert.False(konto.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(AppUser.NormalizedEmail)).IsUnique);
        Assert.True(konto.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(AppUser.NormalizedUserName)).IsUnique);
        Assert.True(db.Model.FindEntityType(typeof(UserSalon))!.GetIndexes().Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(UserSalon.AppUserId)).IsUnique);
    }

    private static ServiceProvider Dienste(IDbContextFactory<ApplicationDbContext> factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<AdminKontenService>(); services.AddScoped<AdminService>(); services.AddScoped<TeamService>();
        return services.BuildServiceProvider();
    }

    private static async Task<AppUser> NutzerAsync(IServiceProvider sp, string rolle, bool erster = false)
    {
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        if (!await rollen.RoleExistsAsync(rolle)) Assert.True((await rollen.CreateAsync(new Role { Name = rolle })).Succeeded);
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var email = $"{Guid.NewGuid():N}@example.test";
        var u = new AppUser { UserName = email, Email = email, FullName = rolle, IstErsterAdmin = erster };
        Assert.True((await users.CreateAsync(u, "Initial123!Ab")).Succeeded);
        Assert.True((await users.AddToRoleAsync(u, rolle)).Succeeded);
        return u;
    }

    [Fact]
    public async Task Profil_Aendert_Nur_Pseudonym_Und_Beendet_Sitzungen()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admin = await NutzerAsync(scope.ServiceProvider, AppRollen.SuperAdmin, true);
        admin.ActiveSessionId = Guid.NewGuid();
        await users.UpdateAsync(admin);
        var email = admin.Email;
        var stamp = admin.SecurityStamp;
        var service = new KontoProfilService(users);
        Assert.False((await service.AenderePseudonymAsync(admin.Id, "nouveau", "faux")).Erfolg);
        Assert.True((await service.AenderePseudonymAsync(admin.Id, " nouveau ", "Initial123!Ab")).Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        var actual = await db.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.Equal("nouveau", actual.UserName);
        Assert.Equal("NOUVEAU", actual.NormalizedUserName);
        Assert.Equal(email, actual.Email);
        Assert.True(actual.IstErsterAdmin);
        Assert.Null(actual.ActiveSessionId);
        Assert.NotEqual(stamp, actual.SecurityStamp);
        Assert.True(await users.CheckPasswordAsync(actual, "Initial123!Ab"));
        var profil = await service.GetAsync(admin.Id);
        Assert.Equal("nouveau", profil!.Pseudonym);
        Assert.Equal(email, profil.Email);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("nom avec espace")]
    [InlineData("nom@example.test")]
    [InlineData("reserve")]
    [InlineData("RESERVE")]
    public async Task Profil_Verweigert_Ungueltige_Und_Belegte_Namen(string name)
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await NutzerAsync(scope.ServiceProvider, AppRollen.Besitzer);
        var other = await NutzerAsync(scope.ServiceProvider, AppRollen.Besitzer);
        Assert.True((await users.SetUserNameAsync(other, "reserve")).Succeeded);
        Assert.False((await new KontoProfilService(users).AenderePseudonymAsync(owner.Id, name, "Initial123!Ab")).Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(owner.Email, (await db.Users.SingleAsync(u => u.Id == owner.Id)).UserName);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Profil_Verweigert_Gesperrte_Und_Interne_Konten(bool gesperrt, bool intern)
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await NutzerAsync(scope.ServiceProvider, AppRollen.Besitzer);
        user.IsBlocked = gesperrt; user.IstInternerKassierer = intern;
        await users.UpdateAsync(user);
        var service = new KontoProfilService(users);
        Assert.Null(await service.GetAsync(user.Id));
        Assert.False((await service.AenderePseudonymAsync(user.Id, "nouveau", "Initial123!Ab")).Erfolg);
    }

    [Fact]
    public async Task Gleiche_Email_Mit_Unterschiedlichen_Pseudonymen_Erlaubt()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var admin = await NutzerAsync(sp, AppRollen.SuperAdmin, true);
        var service = sp.GetRequiredService<AdminService>();
        await NutzerAsync(sp, AppRollen.Besitzer);
        Assert.True((await service.LegeBesitzerMitSalonAnAsync(admin.Id, "A", "Test", "partage@example.test", "Initial123!Ab", "Boutique A", null, benutzername: "boutique_a")).Erfolg);
        Assert.True((await service.LegeBesitzerMitSalonAnAsync(admin.Id, "A", "Test", "partage@example.test", "Initial123!Ab", "Boutique B", null, benutzername: "boutique_b")).Erfolg);
        Assert.False((await service.LegeBesitzerMitSalonAnAsync(admin.Id, "A", "Test", "autre@example.test", "Initial123!Ab", "Boutique C", null, benutzername: "boutique_a")).Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Users.CountAsync(u => u.Email == "partage@example.test"));
        Assert.Equal(2, await db.UserSalons.CountAsync());
    }

    [Fact]
    public async Task Admin_Darf_Admin_Anlegen_Aber_Nur_Erster_Darf_Andere_Aendern_Und_Loeschen()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var erster = await NutzerAsync(sp, AppRollen.SuperAdmin, true);
        var zweiter = await NutzerAsync(sp, AppRollen.SuperAdmin);
        var po = await NutzerAsync(sp, AppRollen.ProduktOwner);
        var s = sp.GetRequiredService<AdminKontenService>();
        await s.LegeAnAsync(zweiter.Id, "Autre", "Admin", "admin@example.test", "Initial123!Ab");
        var neu = (await s.GetAsync(erster.Id)).Single(u => u.Email == "admin@example.test");
        Assert.False(neu.ErsterAdmin);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.SpeichereAsync(zweiter.Id, neu.Id, "X", "Y", neu.Email, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.LoescheAsync(zweiter.Id, neu.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.LegeAnAsync(po.Id, "X", "Y", "falsch@example.test", "Initial123!Ab"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.LoescheAsync(erster.Id, erster.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.SpeichereAsync(erster.Id, erster.Id, "X", "Y", erster.Email!, true));
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var konto = (await users.FindByIdAsync(neu.Id.ToString()))!;
        konto.ActiveSessionId = Guid.NewGuid(); await users.UpdateAsync(konto);
        await s.SpeichereAsync(erster.Id, neu.Id, "Nouveau", "Nom", "nouveau@example.test", true);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var gespeichert = await db.Users.SingleAsync(u => u.Id == neu.Id);
            Assert.True(gespeichert.IsBlocked); Assert.Null(gespeichert.ActiveSessionId);
            Assert.Equal("Nouveau Nom", gespeichert.FullName); Assert.Equal("nouveau@example.test", gespeichert.Email);
        }
        await s.LoescheAsync(erster.Id, neu.Id);
        Assert.DoesNotContain(await s.GetAsync(erster.Id), u => u.Id == neu.Id);
    }

    [Fact]
    public async Task Gesperrter_Admin_Und_Nichtadmin_Duerfen_Keine_Admins_Verwalten()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var admin = await NutzerAsync(sp, AppRollen.SuperAdmin, true);
        admin.IsBlocked = true; await sp.GetRequiredService<UserManager<AppUser>>().UpdateAsync(admin);
        var s = sp.GetRequiredService<AdminKontenService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.GetAsync(admin.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.LegeAnAsync(admin.Id, "X", "Y", "neu@example.test", "Initial123!Ab"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Po_Legt_Je_Neuen_Besitzer_Einen_Ersten_Laden_Mit_Betreuung_An()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var po = await NutzerAsync(sp, AppRollen.ProduktOwner);
        await NutzerAsync(sp, AppRollen.Besitzer);
        var s = sp.GetRequiredService<AdminService>();
        var (ok, fehler, besitzer) = await s.LegeBesitzerMitSalonAnAsync(po.Id, "Jean", "Test", "owner@example.test", "Initial123!Ab", "Boutique A", "Lomé", "Togo");
        Assert.True(ok, fehler); Assert.NotNull(besitzer);
        Assert.False((await s.LegeBesitzerMitSalonAnAsync(po.Id, "Jean", "Test", "owner@example.test", "Initial123!Ab", "Boutique B", null)).Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        var laden = Assert.Single(await db.Salons.ToListAsync());
        Assert.NotNull(laden.ProbeBisUtc); Assert.True(laden.IstFreigegeben);
        Assert.Equal(besitzer.Id, Assert.Single(await db.UserSalons.ToListAsync()).AppUserId);
        var betreuung = Assert.Single(await db.BesitzerBetreuungen.ToListAsync());
        Assert.Equal(po.Id, betreuung.ProduktOwnerId); Assert.Equal(besitzer.Id, betreuung.BesitzerId);
        Assert.Equal(po.Id, Assert.Single(await db.ProduktOwnerSalons.ToListAsync()).ProduktOwnerId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.LegeProduktOwnerAnAsync(po.Id, "X", "Y", "po2@example.test", "Initial123!Ab"));
    }

    [Fact]
    public async Task Besitzer_Legt_Nur_Im_Eigenen_Laden_Kassierer_An()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var besitzer = await NutzerAsync(sp, AppRollen.Besitzer);
        await NutzerAsync(sp, AppRollen.Kassierer);
        var eigener = new Salon { Name = "Eigener" };
        var fremder = new Salon { Name = "Fremder" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(fremder);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, Salon = eigener }); await db.SaveChangesAsync();
        }
        var s = sp.GetRequiredService<TeamService>();
        Assert.False((await s.LegeKassiererAnAsync(besitzer.Id, fremder.Id, "X", "Y", "1234")).Erfolg);
        var (ok, fehler, kassierer) = await s.LegeKassiererAnAsync(besitzer.Id, eigener.Id, "X", "Y", "0123");
        Assert.True(ok, fehler); Assert.NotNull(kassierer);
        Assert.True(kassierer.IstInternerKassierer); Assert.Null(kassierer.Email); Assert.Null(kassierer.PasswordHash);
        Assert.True(await sp.GetRequiredService<UserManager<AppUser>>().IsInRoleAsync(kassierer, AppRollen.Kassierer));
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.Equal(eigener.Id, (await kontrolle.UserSalons.SingleAsync(u => u.AppUserId == kassierer.Id)).SalonId);
        Assert.False((await s.LegeKassiererAnAsync(kassierer.Id, eigener.Id, "X", "Y", "1234")).Erfolg);
    }

    [Fact]
    public async Task Weitere_Laeden_Erst_Nach_Probezeit_Mit_Besitzerrolle_Und_Freigabe()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var owner = await NutzerAsync(scope.ServiceProvider, AppRollen.Besitzer);
        var salon = new Salon { Name = "Premier", IstFreigegeben = true, ProbeBisUtc = DateTime.UtcNow.AddDays(1) };
        await using (var db = await factory.CreateDbContextAsync())
        { db.UserSalons.Add(new UserSalon { AppUserId = owner.Id, Salon = salon }); await db.SaveChangesAsync(); }
        var s = new SalonService(factory, null!);
        Assert.False(await s.DarfWeiterenLadenAnlegenAsync(owner.Id));
        await using (var db = await factory.CreateDbContextAsync())
        { (await db.Salons.SingleAsync()).ProbeBisUtc = DateTime.UtcNow.AddDays(-1); await db.SaveChangesAsync(); }
        var result = await s.FuegeSalonHinzuAsync(owner.Id, "Deuxième", null, "Togo");
        Assert.False(result.Erfolg); Assert.Null(result.Salon);
        await using (var db = await factory.CreateDbContextAsync())
        { (await db.Users.SingleAsync()).IsBlocked = true; await db.SaveChangesAsync(); }
        Assert.False(await s.DarfWeiterenLadenAnlegenAsync(owner.Id));
        Assert.False((await s.FuegeSalonHinzuAsync(owner.Id, "Troisième", null, null)).Erfolg);
    }

    [Fact]
    public async Task Zustaendiger_Po_Und_Admin_Ergaenzen_Bestehendes_Konto_Ohne_Neue_Probe()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var owner = await NutzerAsync(sp, AppRollen.Besitzer);
        var po = await NutzerAsync(sp, AppRollen.ProduktOwner);
        var fremd = await NutzerAsync(sp, AppRollen.ProduktOwner);
        var admin = await NutzerAsync(sp, AppRollen.SuperAdmin);
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.UserSalons.Add(new UserSalon { AppUserId = owner.Id, Salon = new Salon { Name = "Premier", IstFreigegeben = true, ProbeBisUtc = DateTime.UtcNow.AddDays(-1) } });
            db.BesitzerBetreuungen.Add(new BesitzerBetreuung { BesitzerId = owner.Id, ProduktOwnerId = po.Id });
            await db.SaveChangesAsync();
        }
        var s = new SalonService(factory, null!);
        Assert.Empty(await s.GetBesitzerFuerAnlageAsync(fremd.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.FuegeSalonFuerBesitzerHinzuAsync(fremd.Id, owner.Id, "Faux", null, null));
        var result = await s.FuegeSalonFuerBesitzerHinzuAsync(po.Id, owner.Id, "Deuxième", null, "Togo");
        Assert.False(result.Erfolg); Assert.Null(result.Salon);
        Assert.False((await s.FuegeSalonFuerBesitzerHinzuAsync(admin.Id, owner.Id, "Troisième", null, null)).Erfolg);
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.Equal(1, await kontrolle.UserSalons.CountAsync(us => us.AppUserId == owner.Id));
        Assert.Equal(0, await kontrolle.ProduktOwnerSalons.CountAsync(p => p.ProduktOwnerId == po.Id));
        Assert.Equal(4, await kontrolle.Users.CountAsync());
    }
}
