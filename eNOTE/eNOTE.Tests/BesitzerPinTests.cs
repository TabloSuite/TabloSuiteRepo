using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.AspNetCore.DataProtection;

namespace ENote.Tests;

public class BesitzerPinTests
{
    private static ServiceProvider Dienste(IDbContextFactory<ApplicationDbContext> factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<TeamService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Salon Erster, Salon Zweiter, AppUser Besitzer, Role Rolle)> BasisAsync(
        IDbContextFactory<ApplicationDbContext> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var erster = new Salon { Name = "Salon A", IstFreigegeben = true };
        var zweiter = new Salon { Name = "Salon B", IstFreigegeben = true };
        var besitzer = new AppUser
        {
            Id = Guid.NewGuid(), UserName = "proprietaire@example.test", FullName = "Propriétaire",
            PasswordHash = "unveraenderter-passworthash"
        };
        var rolle = new Role { Id = Guid.NewGuid(), Name = AppRollen.Besitzer, NormalizedName = AppRollen.Besitzer.ToUpperInvariant() };
        db.Roles.Add(rolle);
        db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
        db.UserSalons.AddRange(
            new UserSalon { Salon = erster, AppUser = besitzer, BesitzerPinHash = KassenPinService.HashPin("1111") },
            new UserSalon
            {
                Salon = zweiter, AppUser = besitzer, BesitzerPinHash = KassenPinService.HashPin("2222"),
                KassenPinHash = KassenPinService.HashPin("3333"), PinFehlversuche = 3,
                PinGesperrtBisUtc = DateTime.UtcNow.AddMinutes(5)
            });
        await db.SaveChangesAsync();
        return (erster, zweiter, besitzer, rolle);
    }

    [Fact]
    public async Task SetzeBesitzerPin_Ausgewaehlter_Laden_Bewahrt_Fuehrende_Null_Und_Anmeldepasswort()
    {
        var factory = TestDbContextFactory.Create();
        var (erster, zweiter, besitzer, _) = await BasisAsync(factory);
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();

        var ergebnis = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(zweiter.Id, besitzer.Id, "0707");

        Assert.True(ergebnis.Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        var a = await db.UserSalons.SingleAsync(us => us.SalonId == erster.Id);
        var b = await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
        Assert.True(KassenPinService.PruefePin("1111", a.BesitzerPinHash!));
        Assert.True(KassenPinService.PruefePin("0707", b.BesitzerPinHash!));
        Assert.False(KassenPinService.PruefePin("707", b.BesitzerPinHash!));
        Assert.False(KassenPinService.PruefePin("Passwo", b.BesitzerPinHash!));
        Assert.NotEqual("0707", b.BesitzerPinHash);
        Assert.NotNull(b.BesitzerPinGeschuetzt);
        Assert.DoesNotContain("0707", b.BesitzerPinGeschuetzt!);
        Assert.True(KassenPinService.PruefePin("3333", b.KassenPinHash!));
        Assert.Equal(0, b.PinFehlversuche);
        Assert.Null(b.PinGesperrtBisUtc);
        Assert.Equal("unveraenderter-passworthash", (await db.Users.SingleAsync()).PasswordHash);
    }

    [Theory]
    [InlineData("Passwo")]
    [InlineData("07a7")]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData(" 0707")]
    [InlineData("")]
    public async Task SetzeBesitzerPin_Ungueltige_Eingabe_Veraendert_Keine_Gespeicherte_Pin(string pin)
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();

        var ergebnis = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(zweiter.Id, besitzer.Id, pin);

        Assert.False(ergebnis.Erfolg);
        Assert.Equal("Le code PIN doit contenir 4 à 6 chiffres.", ergebnis.Fehler);
        await using var db = await factory.CreateDbContextAsync();
        var zuordnung = await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
        Assert.True(KassenPinService.PruefePin("2222", zuordnung.BesitzerPinHash!));
        Assert.Equal(3, zuordnung.PinFehlversuche);
        Assert.NotNull(zuordnung.PinGesperrtBisUtc);
        Assert.Equal("unveraenderter-passworthash", (await db.Users.SingleAsync()).PasswordHash);
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    public async Task SetzeBesitzerPin_Prueft_Besitzerrolle_Kontosperre_Zuordnung_Und_Ladenstatus(
        bool besitzerrolle, bool gesperrt, bool zugeordnet, bool aktiv)
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            if (!besitzerrolle) db.UserRoles.RemoveRange(db.UserRoles);
            (await db.Users.SingleAsync()).IsBlocked = gesperrt;
            (await db.Salons.SingleAsync(s => s.Id == zweiter.Id)).IstAktiv = aktiv;
            await db.SaveChangesAsync();
        }
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var benutzerId = zugeordnet ? besitzer.Id : Guid.NewGuid();

        var ergebnis = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(zweiter.Id, benutzerId, "0707");

        Assert.False(ergebnis.Erfolg);
        await using var kontrolle = await factory.CreateDbContextAsync();
        var zuordnung = await kontrolle.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
        Assert.True(KassenPinService.PruefePin("2222", zuordnung.BesitzerPinHash!));
        var abruf = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .GetBesitzerPinAsync(zweiter.Id, benutzerId);
        Assert.Null(abruf.Pin);
        Assert.NotNull(abruf.Fehler);
    }

    [Fact]
    public async Task Gespeicherte_Pin_Ist_In_Neuem_Scope_Abrufbar_Und_Mandantengebunden()
    {
        var factory = TestDbContextFactory.Create();
        var (erster, zweiter, besitzer, _) = await BasisAsync(factory);
        await using var provider = Dienste(factory);
        await using (var scope = provider.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<TeamService>()
                .SetzeBesitzerPinAsync(zweiter.Id, besitzer.Id, "0707")).Erfolg);
        await using var neu = provider.CreateAsyncScope();
        var service = neu.ServiceProvider.GetRequiredService<TeamService>();
        var abruf = await service.GetBesitzerPinAsync(zweiter.Id, besitzer.Id);
        Assert.Equal("0707", abruf.Pin);
        Assert.Null(abruf.Fehler);
        Assert.True((await service.SetzeBesitzerPinAsync(zweiter.Id, besitzer.Id, "0808")).Erfolg);
        Assert.Equal("0808", (await service.GetBesitzerPinAsync(zweiter.Id, besitzer.Id)).Pin);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var quelle = await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
            var ziel = await db.UserSalons.SingleAsync(us => us.SalonId == erster.Id);
            ziel.BesitzerPinGeschuetzt = quelle.BesitzerPinGeschuetzt;
            ziel.BesitzerPinHash = quelle.BesitzerPinHash;
            await db.SaveChangesAsync();
        }
        Assert.Null((await service.GetBesitzerPinAsync(erster.Id, besitzer.Id)).Pin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("beschaedigter-wert")]
    public async Task Alte_Oder_Beschaedigte_Werte_Geben_Keine_Pin_Preis(string? geschuetzt)
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            (await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).BesitzerPinGeschuetzt = geschuetzt;
            await db.SaveChangesAsync();
        }
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var abruf = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .GetBesitzerPinAsync(zweiter.Id, besitzer.Id);
        Assert.Null(abruf.Pin);
        Assert.NotNull(abruf.Fehler);
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.True(KassenPinService.PruefePin("2222", (await kontrolle.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).BesitzerPinHash!));
    }

    [Fact]
    public async Task SetzeBesitzerPin_Ersetzt_Alte_Pin_Statt_Anmeldepasswort()
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TeamService>();

        Assert.True((await service.SetzeBesitzerPinAsync(zweiter.Id, besitzer.Id, "0707")).Erfolg);
        Assert.True((await service.SetzeBesitzerPinAsync(zweiter.Id, besitzer.Id, "0808")).Erfolg);

        await using var db = await factory.CreateDbContextAsync();
        var zuordnung = await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
        Assert.True(KassenPinService.PruefePin("0808", zuordnung.BesitzerPinHash!));
        Assert.False(KassenPinService.PruefePin("0707", zuordnung.BesitzerPinHash!));
        Assert.Equal("unveraenderter-passworthash", (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task SetzeKassenPin_Laesst_BesitzerPin_Unveraendert()
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        Guid zuordnungId;
        await using (var db = await factory.CreateDbContextAsync())
            zuordnungId = (await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).Id;
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();

        Assert.True((await scope.ServiceProvider.GetRequiredService<TeamService>().SetzeKassenPinAsync(besitzer.Id, zweiter.Id, zuordnungId, "4444")).Erfolg);

        await using var kontrolle = await factory.CreateDbContextAsync();
        var zuordnung = await kontrolle.UserSalons.SingleAsync(us => us.Id == zuordnungId);
        Assert.True(KassenPinService.PruefePin("4444", zuordnung.KassenPinHash!));
        Assert.True(KassenPinService.PruefePin("2222", zuordnung.BesitzerPinHash!));
    }
}
