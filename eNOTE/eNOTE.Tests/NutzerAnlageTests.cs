using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
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
        services.AddSingleton(_ => new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance));
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
    public async Task Profil_Aendert_Name_Und_Telefon_Ohne_Abmeldung()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var besitzer = await NutzerAsync(scope.ServiceProvider, AppRollen.Besitzer);
        var sitzung = Guid.NewGuid();
        besitzer.ActiveSessionId = sitzung;
        await users.UpdateAsync(besitzer);
        var service = new KontoProfilService(users);

        Assert.False((await service.AenderePersoenlicheDatenAsync(besitzer.Id, "  ", "Mensah", null)).Erfolg);
        Assert.False((await service.AenderePersoenlicheDatenAsync(besitzer.Id, "Ama", "Mensah", "appelle-moi")).Erfolg);
        Assert.False((await service.AenderePersoenlicheDatenAsync(besitzer.Id, new string('a', 61), "Mensah", null)).Erfolg);
        var (ok, fehler) = await service.AenderePersoenlicheDatenAsync(besitzer.Id, " Ama  Akossiwa ", " Mensah ", " +228 90  12 34 56 ");
        Assert.True(ok, fehler);

        await using var db = await factory.CreateDbContextAsync();
        var konto = await db.Users.SingleAsync(u => u.Id == besitzer.Id);
        Assert.Equal("Ama Akossiwa", konto.Vorname);
        Assert.Equal("Mensah", konto.Nachname);
        Assert.Equal("Ama Akossiwa Mensah", konto.FullName);
        Assert.Equal("+228 90 12 34 56", konto.TelefonNummer);
        Assert.Equal(sitzung, konto.ActiveSessionId); // bleibt angemeldet

        Assert.True((await service.AenderePersoenlicheDatenAsync(besitzer.Id, "Ama", "Mensah", "")).Erfolg);
        Assert.Null((await service.GetAsync(besitzer.Id))!.Telefon);
    }

    [Fact]
    public async Task Profil_Aendert_Email_Nur_Mit_Passwort_Und_Warnt_Alte_Adresse()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var po = await NutzerAsync(scope.ServiceProvider, AppRollen.ProduktOwner);
        var alt = po.Email!;
        var config = new ConfigurationBuilder().Build();
        var mails = new MailBenachrichtigungen(factory, new EmailService(config, factory, NullLogger<EmailService>.Instance), config, NullLogger<MailBenachrichtigungen>.Instance);
        var service = new KontoProfilService(users, mails);

        Assert.Equal("Le mot de passe actuel est incorrect.", (await service.AendereEmailAsync(po.Id, "neu@example.test", "faux")).Fehler);
        Assert.False((await service.AendereEmailAsync(po.Id, "pas-une-adresse", "Initial123!Ab")).Erfolg);
        Assert.False((await service.AendereEmailAsync(po.Id, alt.ToUpperInvariant(), "Initial123!Ab")).Erfolg);
        var (ok, fehler) = await service.AendereEmailAsync(po.Id, "  neu@example.test ", "Initial123!Ab");
        Assert.True(ok, fehler);

        await using var db = await factory.CreateDbContextAsync();
        var konto = await db.Users.SingleAsync(u => u.Id == po.Id);
        Assert.Equal("neu@example.test", konto.Email);
        Assert.Equal("NEU@EXAMPLE.TEST", konto.NormalizedEmail);
        var hinweis = await db.MailNachrichten.SingleAsync(m => m.Kategorie == "email-modifiee");
        Assert.Equal(alt, hinweis.Empfaenger);                    // an die alte Adresse
        Assert.Contains("n•••u@example.test", hinweis.HtmlInhalt); // neue Adresse nur maskiert
        Assert.DoesNotContain("neu@example.test", hinweis.HtmlInhalt);
    }

    [Theory]
    [InlineData("ama.mensah@exemple.com", "a•••h@exemple.com")]
    [InlineData("ab@x.tg", "a•••@x.tg")]
    [InlineData("kein-at", "•••")]
    public void Email_Wird_Fuer_Hinweise_Maskiert(string adresse, string erwartet)
        => Assert.Equal(erwartet, MailBenachrichtigungen.MaskiereEmail(adresse));

    [Fact]
    public async Task Profil_Bleibt_Fuer_Kassierer_Gesperrt()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var kassierer = await NutzerAsync(scope.ServiceProvider, AppRollen.Kassierer);
        var service = new KontoProfilService(users);
        Assert.Null(await service.GetAsync(kassierer.Id));
        Assert.False((await service.AenderePersoenlicheDatenAsync(kassierer.Id, "Awa", "Koffi", null)).Erfolg);
        Assert.False((await service.AendereEmailAsync(kassierer.Id, "awa@example.test", "Initial123!Ab")).Erfolg);
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
        // Nur der Hauptadministrator legt weitere Admins an.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.LegeAnAsync(zweiter.Id, "X", "Y", "zweiter@example.test", "Initial123!Ab"));
        await s.LegeAnAsync(erster.Id, "Autre", "Admin", "admin@example.test", "Initial123!Ab");
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
    public async Task Ohne_Kennzeichnung_Wird_Das_Aelteste_Echte_Adminkonto_Hauptadmin_Und_Kann_Sperren()
    {
        var factory = TestDbContextFactory.Create();
        await using var provider = Dienste(factory);
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var echt = await NutzerAsync(sp, AppRollen.SuperAdmin);
        var demo = await NutzerAsync(sp, AppRollen.SuperAdmin);
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        // Demo-Konto ist älter, darf aber nie Hauptadmin werden
        demo.UserName = "demo.admin"; demo.CreatedAtUtc = echt.CreatedAtUtc.AddDays(-1); await users.UpdateAsync(demo);
        var s = sp.GetRequiredService<AdminKontenService>();
        Assert.True(await s.IstErsterAdminAsync(echt.Id));
        Assert.False(await s.IstErsterAdminAsync(demo.Id));

        await s.SetzeGesperrtAsync(echt.Id, demo.Id, true);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.True((await db.Users.SingleAsync(u => u.Id == demo.Id)).IsBlocked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.SetzeGesperrtAsync(echt.Id, echt.Id, true));
        await s.SetzeGesperrtAsync(echt.Id, demo.Id, false);
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
        var admin = await NutzerAsync(sp, AppRollen.SuperAdmin);
        var po = await NutzerAsync(sp, AppRollen.ProduktOwner);
        // Besitzer-Rolle sicherstellen (LegeBesitzerMitSalonAnAsync weist sie zu).
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        if (!await rollen.RoleExistsAsync(AppRollen.Besitzer)) Assert.True((await rollen.CreateAsync(new Role { Name = AppRollen.Besitzer })).Succeeded);
        var s = sp.GetRequiredService<AdminService>();
        // ProduktOwner darf keine Besitzer mit Salon anlegen (nur SuperAdmin).
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            s.LegeBesitzerMitSalonAnAsync(po.Id, "Jean", "Test", "p@example.test", "Initial123!Ab", "B", null));
        var (ok, fehler, besitzer) = await s.LegeBesitzerMitSalonAnAsync(admin.Id, "Jean", "Test", "owner@example.test", "Initial123!Ab", "Boutique A", "Lomé", "Togo");
        Assert.True(ok, fehler); Assert.NotNull(besitzer);
        // Gleiche E-Mail erneut (gleiche Person) mit neuem Pseudonym ist erlaubt.
        var zweites = await s.LegeBesitzerMitSalonAnAsync(admin.Id, "Jean", "Test", "owner@example.test", "Initial123!Ab", "Boutique B", null, benutzername: "owner.zweit");
        Assert.True(zweites.Erfolg, zweites.Fehler);
        // Gleiches Pseudonym erneut wird abgelehnt.
        Assert.False((await s.LegeBesitzerMitSalonAnAsync(admin.Id, "Jean", "Test", "owner@example.test", "Initial123!Ab", "Boutique C", null, benutzername: "owner.zweit")).Erfolg);
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Salons.CountAsync());
        Assert.True((await db.Salons.SingleAsync(s2 => s2.Name == "Boutique A")).IstFreigegeben);
        // Jedes Konto gehört zu genau einem Laden (Regel: ein Konto pro Laden).
        Assert.Equal(2, await db.UserSalons.CountAsync());
        Assert.Equal(besitzer.Id, (await db.UserSalons.SingleAsync(us => us.Salon.Name == "Boutique A")).AppUserId);
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
        var (ok, fehler, kassierer) = await s.LegeKassiererAnAsync(eigener.Id, "X", "Y", "kassierer.pseudo", "kass2@example.test", "Init123!Ab", "0123");
        Assert.True(ok, fehler); Assert.NotNull(kassierer);
        Assert.Equal("kassierer.pseudo", kassierer.UserName);
        Assert.True(await sp.GetRequiredService<UserManager<AppUser>>().IsInRoleAsync(kassierer, AppRollen.Kassierer));
        await using var kontrolle = await factory.CreateDbContextAsync();
        var zuordnung = await kontrolle.UserSalons.SingleAsync(u => u.AppUserId == kassierer.Id);
        Assert.Equal(eigener.Id, zuordnung.SalonId);
        Assert.NotNull(zuordnung.KassenPinHash);
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
        var s = new SalonService(factory, null!, scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
        // Während der laufenden Probezeit ist kein weiterer Laden erlaubt.
        Assert.False(await s.DarfWeiterenLadenAnlegenAsync(owner.Id));
        Assert.False((await s.FuegeSalonHinzuAsync(owner.Id, "Trop tôt", null, null, "A", "B", "pseudo.zu1", "a@b.test", "Init123!Ab")).Erfolg);
        await using (var db = await factory.CreateDbContextAsync())
        { (await db.Salons.SingleAsync()).ProbeBisUtc = DateTime.UtcNow.AddDays(-1); await db.SaveChangesAsync(); }
        // Nach abgelaufener Probezeit eines freigegebenen Ladens darf ein weiterer angelegt werden.
        Assert.True(await s.DarfWeiterenLadenAnlegenAsync(owner.Id));
        var result = await s.FuegeSalonHinzuAsync(owner.Id, "Deuxième", null, "Togo", "A", "B", "pseudo.zu2", "a@b.test", "Init123!Ab");
        Assert.True(result.Erfolg); Assert.NotNull(result.Salon); Assert.NotNull(result.Konto);
        // Gleiche E-Mail erneut (gleiche Person) ist erlaubt, neues Pseudonym nötig.
        Assert.False((await s.FuegeSalonHinzuAsync(owner.Id, "Troisième", null, null, "A", "B", "pseudo.zu2", "a@b.test", "Init123!Ab")).Erfolg);
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
        var s = new SalonService(factory, null!, scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
        // Nach abgelaufener Probezeit darf ein weiterer Laden mit neuem Konto angelegt werden.
        var result = await s.FuegeSalonHinzuAsync(owner.Id, "Deuxième", null, "Togo", "A", "B", "pseudo.neu", "gleich@mail.test", "Init123!Ab");
        Assert.True(result.Erfolg); Assert.NotNull(result.Salon);
        await using var kontrolle = await factory.CreateDbContextAsync();
        // Der neue Laden gehört einem neuen Konto (Regel: ein Konto pro Laden).
        Assert.Equal(2, await kontrolle.UserSalons.CountAsync());
        Assert.NotEqual(owner.Id, result.Konto!.Id);
        Assert.Equal(result.Konto.Id, (await kontrolle.UserSalons.SingleAsync(us => us.SalonId == result.Salon!.Id)).AppUserId);
        Assert.Equal(5, await kontrolle.Users.CountAsync());
    }
}
