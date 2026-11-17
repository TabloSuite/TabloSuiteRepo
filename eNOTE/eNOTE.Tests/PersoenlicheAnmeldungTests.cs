using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services.Kasse;
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
/// Nutzerwunsch 17.11.2026 « Connexion personnelle »: Mitarbeitende haben standardmäßig kein Passwort und kassieren am Gerät
/// des Besitzers (Name + PIN). Nur mit diesem Recht melden sie sich selbst an – dann nur ihre Bereiche, nie Kasse oder Besitzer-PIN.
/// </summary>
public class PersoenlicheAnmeldungTests
{
    private static ServiceProvider Dienste(IDbContextFactory<ApplicationDbContext> factory)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(factory);
        services.AddTransient(_ => factory.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton(_ => new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance));
        services.AddScoped<TeamService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(ServiceProvider Provider, TeamService Team, UserManager<AppUser> Users, Guid Salon, AppUser Besitzer)> BasisAsync(
        IDbContextFactory<ApplicationDbContext> factory)
    {
        var provider = Dienste(factory);
        var sp = provider.CreateAsyncScope().ServiceProvider;
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        foreach (var r in new[] { AppRollen.Besitzer, AppRollen.Kassierer })
            Assert.True((await rollen.CreateAsync(new Role { Name = r })).Succeeded);
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var besitzer = new AppUser { UserName = "proprio", Email = "p@example.test", FullName = "Ama" };
        Assert.True((await users.CreateAsync(besitzer, "Initial123!Ab")).Succeeded);
        Assert.True((await users.AddToRoleAsync(besitzer, AppRollen.Besitzer)).Succeeded);
        var salon = new Salon { Name = "Boutique", IstAktiv = true, IstFreigegeben = true };
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id, BesitzerPinHash = KassenPinService.HashPin("1234") });
            await db.SaveChangesAsync();
        }
        return (provider, sp.GetRequiredService<TeamService>(), users, salon.Id, besitzer);
    }

    [Fact]
    public async Task Neuer_Mitarbeiter_Ohne_Passwort_Darf_Sich_Nicht_Anmelden_Besitzer_Schon()
    {
        var factory = TestDbContextFactory.Create();
        var (provider, team, users, salon, besitzer) = await BasisAsync(factory);
        await using var _ = provider;
        var (ok, fehler, awa) = await team.LegeKassiererAnAsync(salon, "Awa", "Koffi", "awa.koffi", "awa@example.test", null, "2222");
        Assert.True(ok, fehler);
        Assert.False(await users.HasPasswordAsync(awa!));

        await using var db = await factory.CreateDbContextAsync();
        Assert.False((await db.UserSalons.SingleAsync(us => us.AppUserId == awa!.Id)).KannSichAnmelden);
        Assert.True(await LadenZugriff.IstMitarbeiterKontoAsync(db, awa!.Id));
        Assert.False(await LadenZugriff.DarfSichAnmeldenAsync(db, awa.Id));
        Assert.False(await LadenZugriff.IstMitarbeiterKontoAsync(db, besitzer.Id));
        Assert.True(await LadenZugriff.DarfSichAnmeldenAsync(db, besitzer.Id));
    }

    [Fact]
    public async Task Recht_Einschalten_Braucht_Passwort_Ausschalten_Beendet_Die_Sitzung()
    {
        var factory = TestDbContextFactory.Create();
        var (provider, team, users, salon, besitzer) = await BasisAsync(factory);
        await using var _ = provider;
        var (_, _, kofi) = await team.LegeKassiererAnAsync(salon, "Kofi", "Agbeko", "kofi", "kofi@example.test", null, "3333");
        Guid zuordnung;
        await using (var db = await factory.CreateDbContextAsync())
            zuordnung = (await db.UserSalons.SingleAsync(us => us.AppUserId == kofi!.Id)).Id;

        // Ohne Passwort lässt sich das Recht nicht einschalten.
        var ohne = await team.SetzeAnmeldungAsync(zuordnung, true, null);
        Assert.False(ohne.Erfolg);
        // Mit Passwort: Konto kann sich anmelden.
        var mit = await team.SetzeAnmeldungAsync(zuordnung, true, "Kofi!2026a");
        Assert.True(mit.Erfolg, mit.Fehler);
        var konto = await users.FindByIdAsync(kofi!.Id.ToString());
        Assert.True(await users.CheckPasswordAsync(konto!, "Kofi!2026a"));
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.True(await LadenZugriff.DarfSichAnmeldenAsync(db, kofi.Id));
            (await db.Users.SingleAsync(u => u.Id == kofi.Id)).ActiveSessionId = Guid.NewGuid(); // angemeldet
            await db.SaveChangesAsync();
        }

        // Ausschalten: keine Anmeldung mehr, laufende Sitzung beendet.
        Assert.True((await team.SetzeAnmeldungAsync(zuordnung, false, null)).Erfolg);
        await using (var db = await factory.CreateDbContextAsync())
        {
            Assert.False(await LadenZugriff.DarfSichAnmeldenAsync(db, kofi.Id));
            Assert.Null((await db.Users.SingleAsync(u => u.Id == kofi.Id)).ActiveSessionId);
        }

        // Für den Besitzer gibt es das Recht nicht (er meldet sich immer an).
        Guid besitzerZuordnung;
        await using (var db = await factory.CreateDbContextAsync())
            besitzerZuordnung = (await db.UserSalons.SingleAsync(us => us.AppUserId == besitzer.Id)).Id;
        Assert.False((await team.SetzeAnmeldungAsync(besitzerZuordnung, false, null)).Erfolg);
    }

    [Fact]
    public async Task Besitzer_PIN_Wird_Von_Einem_Mitarbeiter_Konto_Nie_Angenommen()
    {
        var factory = TestDbContextFactory.Create();
        var (provider, team, _, salon, besitzer) = await BasisAsync(factory);
        await using var _ = provider;
        var (_, _, kofi) = await team.LegeKassiererAnAsync(salon, "Kofi", "Agbeko", "kofi", "kofi@example.test", "Kofi!2026a", "3333");
        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService());

        // Am Gerät des Besitzers (Besitzer-Konto) gilt die richtige PIN …
        Assert.Null(await kasse.PruefeBesitzerFreigabeAsync(salon, besitzer.Id, "1234"));
        // … vom eigenen Mitarbeiter-Konto aus nie, auch nicht mit der richtigen PIN.
        Assert.Equal(LadenZugriff.BesitzerPinNurBeimBesitzerText, await kasse.PruefeBesitzerFreigabeAsync(salon, kofi!.Id, "1234"));
    }

    [Fact]
    public async Task Equipe_Zaehlt_Verkaeufe_Fuer_Die_Kassierende_Person()
    {
        // « Qui encaisse ? »: Awa öffnet die Kasse, Kofi kassiert an einem zweiten Gerät – der Verkauf zählt für Kofi.
        var factory = TestDbContextFactory.Create();
        var (provider, team, _, salon, _) = await BasisAsync(factory);
        await using var _ = provider;
        var (_, _, awa) = await team.LegeKassiererAnAsync(salon, "Awa", "Koffi", "awa", "awa@example.test", null, "2222");
        var (_, _, kofi) = await team.LegeKassiererAnAsync(salon, "Kofi", "Agbeko", "kofi", "kofi@example.test", null, "3333");
        var artikel = new ENote.Core.DataModels.Katalog.Leistung { SalonId = salon, Name = "Café", Preis = 500, IstAktiv = true };
        await using (var db = await factory.CreateDbContextAsync()) { db.Leistungen.Add(artikel); await db.SaveChangesAsync(); }
        var kasse = new KassenService(factory, new FakeUmsatzBenachrichtigungService());
        var sitzung = (await kasse.StarteSitzungAsync(salon, awa!.Id, 1000)).Sitzung!;
        var (ok, fehler, _, _) = await kasse.BucheVerkaufAsync(sitzung.Id, [new KassenService.VerkaufsPositionInput(artikel.Id, 1, null, false)],
            [new KassenService.ZahlungInput(ENote.Core.DataModels.Kasse.ZahlungsArt.Bar, Gegeben: 500)], kassiererId: kofi!.Id);
        Assert.True(ok, fehler);

        var mitglieder = await team.GetTeamAsync(salon);
        var k = mitglieder.Single(m => m.UserId == kofi.Id);
        Assert.Equal(1, k.VerkaeufeMonat);
        Assert.NotNull(k.LetzteKasseUtc); // eingestiegen, nicht geöffnet – zählt trotzdem als Aktivität
        Assert.Equal(0, mitglieder.Single(m => m.UserId == awa.Id).VerkaeufeMonat);
    }

    [Fact]
    public async Task Anlage_Mit_Passwort_Gibt_Gleich_Die_Persoenliche_Anmeldung()
    {
        var factory = TestDbContextFactory.Create();
        var (provider, team, _, salon, _) = await BasisAsync(factory);
        await using var _ = provider;
        var (ok, fehler, emp) = await team.LegeKassiererAnAsync(salon, "Yao", "K", "yao", "yao@example.test", "Yao!2026ab", "4444");
        Assert.True(ok, fehler);
        await using var db = await factory.CreateDbContextAsync();
        Assert.True(await LadenZugriff.DarfSichAnmeldenAsync(db, emp!.Id));
        var mitglied = (await team.GetTeamAsync(salon)).Single(m => m.UserId == emp.Id);
        Assert.True(mitglied.KannSichAnmelden);
        Assert.True(mitglied.HatPasswort);
    }
}
