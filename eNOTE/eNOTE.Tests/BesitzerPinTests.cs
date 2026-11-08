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

        Guid zuordnungB;
        await using (var dbIds = await factory.CreateDbContextAsync())
            zuordnungB = (await dbIds.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).Id;

        var ergebnis = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(zuordnungB, "0707");

        Assert.True(ergebnis.Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        var a = await db.UserSalons.SingleAsync(us => us.SalonId == erster.Id);
        var b = await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
        Assert.True(KassenPinService.PruefePin("1111", a.BesitzerPinHash!));
        Assert.True(KassenPinService.PruefePin("0707", b.BesitzerPinHash!));
        Assert.False(KassenPinService.PruefePin("707", b.BesitzerPinHash!));
        Assert.False(KassenPinService.PruefePin("Passwo", b.BesitzerPinHash!));
        Assert.NotEqual("0707", b.BesitzerPinHash);
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
        Guid zuordnungId;
        await using (var dbIds = await factory.CreateDbContextAsync())
            zuordnungId = (await dbIds.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).Id;

        var ergebnis = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(zuordnungId, pin);

        Assert.False(ergebnis.Erfolg);
        Assert.Equal("Le code PIN doit contenir 4 à 6 chiffres.", ergebnis.Fehler);
        await using var db = await factory.CreateDbContextAsync();
        var zuordnung = await db.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id);
        Assert.True(KassenPinService.PruefePin("2222", zuordnung.BesitzerPinHash!));
        Assert.Equal(3, zuordnung.PinFehlversuche);
        Assert.NotNull(zuordnung.PinGesperrtBisUtc);
        Assert.Equal("unveraenderter-passworthash", (await db.Users.SingleAsync()).PasswordHash);
    }

    [Fact]
    public async Task SetzeBesitzerPin_Mit_Unbekannter_Zuordnung_Schlaegt_Fehl()
    {
        var factory = TestDbContextFactory.Create();
        await BasisAsync(factory);
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();

        var ergebnis = await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(Guid.NewGuid(), "0707");

        Assert.False(ergebnis.Erfolg);
        Assert.Equal("Membre introuvable.", ergebnis.Fehler);
    }

    [Fact]
    public async Task Gespeicherte_Pin_Ist_In_Neuem_Scope_Wirksam_Und_Mandantengebunden()
    {
        var factory = TestDbContextFactory.Create();
        var (erster, zweiter, besitzer, _) = await BasisAsync(factory);
        Guid idErster, idZweiter;
        await using (var dbIds = await factory.CreateDbContextAsync())
        {
            idErster = (await dbIds.UserSalons.SingleAsync(us => us.SalonId == erster.Id)).Id;
            idZweiter = (await dbIds.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).Id;
        }
        await using var provider = Dienste(factory);
        await using (var scope = provider.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<TeamService>()
                .SetzeBesitzerPinAsync(idZweiter, "0707")).Erfolg);
        await using var neu = provider.CreateAsyncScope();
        var service = neu.ServiceProvider.GetRequiredService<TeamService>();

        await using (var pruef = await factory.CreateDbContextAsync())
            Assert.True(KassenPinService.PruefePin("0707",
                (await pruef.UserSalons.SingleAsync(us => us.Id == idZweiter)).BesitzerPinHash!));

        Assert.True((await service.SetzeBesitzerPinAsync(idZweiter, "0808")).Erfolg);
        await using var kontrolle = await factory.CreateDbContextAsync();
        Assert.True(KassenPinService.PruefePin("0808",
            (await kontrolle.UserSalons.SingleAsync(us => us.Id == idZweiter)).BesitzerPinHash!));
        // Die Zuordnung des anderen Ladens bleibt unverändert (Mandantentrennung).
        Assert.True(KassenPinService.PruefePin("1111",
            (await kontrolle.UserSalons.SingleAsync(us => us.Id == idErster)).BesitzerPinHash!));
    }

    [Fact]
    public async Task Gespeicherter_Hash_Bleibt_Nach_Setzen_Konsistent_Prüfbar()
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        Guid idZweiter;
        await using (var dbIds = await factory.CreateDbContextAsync())
            idZweiter = (await dbIds.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).Id;
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<TeamService>()
            .SetzeBesitzerPinAsync(idZweiter, "0707")).Erfolg);
        await using var kontrolle = await factory.CreateDbContextAsync();
        var zuordnung = await kontrolle.UserSalons.SingleAsync(us => us.Id == idZweiter);
        Assert.True(KassenPinService.PruefePin("0707", zuordnung.BesitzerPinHash!));
    }

    [Fact]
    public async Task SetzeBesitzerPin_Ersetzt_Alte_Pin_Statt_Anmeldepasswort()
    {
        var factory = TestDbContextFactory.Create();
        var (_, zweiter, besitzer, _) = await BasisAsync(factory);
        Guid idZweiter;
        await using (var dbIds = await factory.CreateDbContextAsync())
            idZweiter = (await dbIds.UserSalons.SingleAsync(us => us.SalonId == zweiter.Id)).Id;
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TeamService>();

        Assert.True((await service.SetzeBesitzerPinAsync(idZweiter, "0707")).Erfolg);
        Assert.True((await service.SetzeBesitzerPinAsync(idZweiter, "0808")).Erfolg);

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

        Assert.True((await scope.ServiceProvider.GetRequiredService<TeamService>().SetzeKassenPinAsync(zuordnungId, "4444")).Erfolg);

        await using var kontrolle = await factory.CreateDbContextAsync();
        var zuordnung = await kontrolle.UserSalons.SingleAsync(us => us.Id == zuordnungId);
        Assert.True(KassenPinService.PruefePin("4444", zuordnung.KassenPinHash!));
        Assert.True(KassenPinService.PruefePin("2222", zuordnung.BesitzerPinHash!));
    }

    [Fact]
    public void PinState_Startet_Gesperrt_Und_Schaltet_Um()
    {
        // Standard nach Anmeldung: Verwaltungszone gesperrt, erst die PIN schaltet frei.
        var state = new ENote.WebApp.Services.BesitzerPinState();
        Assert.False(state.VerwaltungFreigeschaltet);

        state.Freischalten();
        Assert.True(state.VerwaltungFreigeschaltet);

        state.Sperren();
        Assert.False(state.VerwaltungFreigeschaltet);
    }

    [Fact]
    public void PinState_Loest_OnChange_Bei_Freischalten_Und_Sperren_Aus()
    {
        var state = new ENote.WebApp.Services.BesitzerPinState();
        var aufrufe = 0;
        state.OnChange += () => aufrufe++;

        state.Freischalten();
        state.Sperren();

        Assert.Equal(2, aufrufe);
    }
}
