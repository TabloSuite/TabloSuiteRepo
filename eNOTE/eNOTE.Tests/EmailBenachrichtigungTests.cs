using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

public class EmailBenachrichtigungTests
{
    private static readonly DateTime Start = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private sealed record Basis(IDbContextFactory<ApplicationDbContext> F, Salon Salon, Uhr Uhr, MailBenachrichtigungen Mails);

    private static async Task<Basis> BasisAsync(DateTime? probeBis = null)
    {
        var factory = TestDbContextFactory.Create();
        var salon = new Salon { Name = "Salon Test", IstAktiv = true, IstFreigegeben = true, ProbeBisUtc = probeBis };
        var besitzer = new AppUser { Id = Guid.NewGuid(), FullName = "Ama Mensah", Vorname = "Ama", UserName = "ama", Email = "ama@exemple.com" };
        await using (var db = await factory.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
            db.Roles.Add(rolle);
            db.Users.Add(besitzer);
            db.UserRoles.Add(new AppUserRole { UserId = besitzer.Id, RoleId = rolle.Id });
            db.Salons.Add(salon);
            db.UserSalons.Add(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id });
            await db.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, factory, NullLogger<EmailService>.Instance);
        var uhr = new Uhr(Start);
        return new Basis(factory, salon, uhr, new MailBenachrichtigungen(factory, email, config, NullLogger<MailBenachrichtigungen>.Instance, uhr));
    }

    private static async Task SchalteEinAsync(Basis b, string kategorie, bool aktiv = true)
    {
        await using var db = await b.F.CreateDbContextAsync();
        db.MailRegeln.Add(new ENote.Core.DataModels.Infrastruktur.MailRegel { Kategorie = kategorie, Aktiv = aktiv });
        await db.SaveChangesAsync();
    }

    private static async Task<List<string?>> KategorienAsync(Basis b)
    {
        await using var db = await b.F.CreateDbContextAsync();
        return await db.MailNachrichten.OrderBy(m => m.CreatedAtUtc).Select(m => m.Kategorie).ToListAsync();
    }

    private static async Task<NutzungsAbo> RechnungAsync(Basis b, DateTime erstellt)
    {
        var abo = new NutzungsAbo
        {
            SalonId = b.Salon.Id, Intervall = AboIntervall.Monatlich, PreisSnapshot = 5000, CreatedAtUtc = erstellt,
            ZeitraumVonUtc = erstellt.Date, ZeitraumBisUtc = erstellt.Date.AddMonths(1).AddTicks(-1),
            FaelligUtc = erstellt.Date, KassenSperreAbUtc = erstellt.Date.AddDays(15)
        };
        await using var db = await b.F.CreateDbContextAsync();
        db.NutzungsAbos.Add(abo);
        await db.SaveChangesAsync();
        return abo;
    }

    [Fact]
    public async Task Rechnung_Erinnerung_Und_Sperre_Werden_Je_Genau_Einmal_Versendet()
    {
        var b = await BasisAsync();
        await RechnungAsync(b, Start);

        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());          // idempotent

        b.Uhr.Jetzt = Start.Date.AddDays(12).AddHours(9);              // 3 Tage vor der Sperre
        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());

        b.Uhr.Jetzt = Start.Date.AddDays(15).AddHours(1);              // Sperre aktiv
        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());

        Assert.Equal(["facture", "rappel-paiement", "caisse-suspendue"], await KategorienAsync(b));
        await using var db = await b.F.CreateDbContextAsync();
        Assert.All(await db.MailNachrichten.ToListAsync(), m => Assert.Equal("ama@exemple.com", m.Empfaenger));
    }

    [Fact]
    public async Task Alte_Rechnungen_Loesen_Beim_Einschalten_Keine_Rechnungsmail_Aus()
    {
        var b = await BasisAsync();
        var alt = await RechnungAsync(b, Start.AddDays(-5));

        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());
        await using var db = await b.F.CreateDbContextAsync();
        Assert.NotNull((await db.NutzungsAbos.SingleAsync(a => a.Id == alt.Id)).RechnungMailUtc);
        Assert.Empty(await db.MailNachrichten.ToListAsync());
    }

    [Fact]
    public async Task Bezahlte_Rechnung_Bekommt_Keine_Erinnerung()
    {
        var b = await BasisAsync();
        var abo = await RechnungAsync(b, Start);
        await b.Mails.PruefeAboMailsAsync();

        await new AboService(b.F, b.Mails).SetzeZahlungsStatusAsync(abo.Id, AboZahlungStatus.Bestaetigt);
        b.Uhr.Jetzt = Start.Date.AddDays(16);
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());

        // « Paiement confirmé » ist standardmäßig abgeschaltet (Kontingent sparen)
        Assert.Equal(["facture"], await KategorienAsync(b));
    }

    [Fact]
    public async Task Probe_Ende_Wird_Sieben_Tage_Vorher_Einmal_Angekuendigt()
    {
        var b = await BasisAsync(probeBis: Start.AddDays(10));
        await SchalteEinAsync(b, "fin-essai");                         // standardmäßig aus
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());           // noch 10 Tage

        b.Uhr.Jetzt = Start.AddDays(4);                                 // noch 6 Tage
        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(0, await b.Mails.PruefeAboMailsAsync());
        Assert.Equal(["fin-essai"], await KategorienAsync(b));
    }

    [Fact]
    public async Task Ohne_Konfiguration_Bleiben_Mails_In_Der_Queue()
    {
        var b = await BasisAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, b.F, NullLogger<EmailService>.Instance);

        Assert.False(email.IstKonfiguriert);
        Assert.False(await email.SendeAsync("test@exemple.com", "Test", "<p>Hallo</p>", "test"));
        Assert.False(await email.SendeAsync("pas-une-adresse", "Test", "<p>Hallo</p>", "test"));

        await using var db = await b.F.CreateDbContextAsync();
        var m = await db.MailNachrichten.SingleAsync();
        Assert.Equal((false, "test"), (m.IstGesendet, m.Kategorie));
    }

    [Fact]
    public void Konfiguration_Waehlt_Den_Versandweg()
    {
        EmailService Mit(params (string, string)[] werte) => new(
            new ConfigurationBuilder().AddInMemoryCollection(werte.ToDictionary(w => w.Item1, w => (string?)w.Item2)).Build(),
            null!, NullLogger<EmailService>.Instance);

        Assert.Equal("Aucun", Mit(("Mail:Provider", "Postmark")).Anbieter);              // Token fehlt
        Assert.Equal("Postmark", Mit(("Mail:Provider", "Postmark"), ("Mail:PostmarkServerToken", "x")).Anbieter);
        Assert.Equal("Smtp", Mit(("Mail:SmtpHost", "smtp.exemple.com")).Anbieter);
        Assert.Equal("Aucun", Mit().Anbieter);
    }

    [Fact]
    public void Textfassung_Behaelt_Links_Und_Entfernt_Html()
    {
        var text = EmailService.AlsText(TabloSuiteMails.RappelPaiement("Ama", "Salon Test", 5000, Start.AddDays(3), "https://enote.tablosuite.com/abonnement"));

        Assert.DoesNotContain("<", text);
        Assert.Contains("Bonjour Ama,", text);
        Assert.Contains("F CFA", text);
        Assert.Contains("(https://enote.tablosuite.com/abonnement)", text);
        Assert.Contains("Montant : ", text);                       // Label und Wert getrennt
        Assert.DoesNotContain(text.Split('\n'), zeile => zeile.Trim() == "T");   // keine Logo-Kachel
        Assert.False(text.StartsWith("La caisse sera suspendue"));  // keine versteckte Vorschauzeile
    }

    [Fact]
    public async Task Abgeschaltete_Mails_Werden_Nicht_Versendet_Pflichtmails_Immer()
    {
        var b = await BasisAsync(probeBis: Start.AddDays(5));
        await RechnungAsync(b, Start);
        await SchalteEinAsync(b, "facture", aktiv: false);            // Pflicht: Schalter wird ignoriert

        Assert.Equal(1, await b.Mails.PruefeAboMailsAsync());           // Rechnung ja, Probe-Ende (aus) nein
        Assert.Equal(["facture"], await KategorienAsync(b));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, b.F, NullLogger<EmailService>.Instance);
        Assert.True(await email.IstAktivAsync("reinitialisation"));
        Assert.True(await email.IstAktivAsync("message"));
        Assert.False(await email.IstAktivAsync("bienvenue"));
        Assert.True(await email.IstAktivAsync("rappel-paiement"));
        Assert.False(await email.SendeAsync("ama@exemple.com", "Bienvenue", "<p>x</p>", "bienvenue"));
        await using var db = await b.F.CreateDbContextAsync();
        Assert.DoesNotContain(await db.MailNachrichten.ToListAsync(), m => m.Kategorie == "bienvenue");
    }

    [Fact]
    public async Task Pflichtmail_Laesst_Sich_Nicht_Abschalten_Und_Nur_Admin_Darf_Schalten()
    {
        var b = await BasisAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, b.F, NullLogger<EmailService>.Instance);
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin", Email = "admin@exemple.com" };
        await using (var db = await b.F.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.SuperAdmin, NormalizedName = "SUPERADMIN" };
            db.Roles.Add(rolle);
            db.Users.Add(admin);
            db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = rolle.Id });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => email.SetzeSchalterAsync(admin.Id, "facture", false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => email.SetzeSchalterAsync(Guid.NewGuid(), "bienvenue", true));
        await email.SetzeSchalterAsync(admin.Id, "bienvenue", true);
        Assert.True((await email.GetSchalterAsync())["bienvenue"]);
        Assert.True(await email.IstAktivAsync("bienvenue"));
    }

    [Fact]
    public async Task Mot_De_Passe_Modifie_Ist_Standardmaessig_An_Ausdruecklich_Abgeschaltet_Bleibt_Aus()
    {
        // Nutzerentscheidung 22.11.2026: Sicherheitsmail standardmäßig an. Ohne gespeicherten Schalter gilt der neue Standard.
        var b = await BasisAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new EmailService(config, b.F, NullLogger<EmailService>.Instance);
        Assert.True(MailArten.Finde("mot-de-passe")!.StandardAktiv);
        Assert.True(await email.IstAktivAsync("mot-de-passe"));
        Assert.True((await email.GetSchalterAsync())["mot-de-passe"]);

        await b.Mails.PasswortGeaendertAsync(await BesitzerIdAsync(b));
        Assert.Equal(["mot-de-passe"], await KategorienAsync(b));

        // Hat der Admin die Mail ausdrücklich ausgeschaltet, bleibt sie aus.
        await SchalteEinAsync(b, "mot-de-passe", aktiv: false);
        Assert.False(await email.IstAktivAsync("mot-de-passe"));
        await b.Mails.PasswortGeaendertAsync(await BesitzerIdAsync(b));
        Assert.Equal(["mot-de-passe"], await KategorienAsync(b));
    }

    private static async Task<Guid> BesitzerIdAsync(Basis b)
    {
        await using var db = await b.F.CreateDbContextAsync();
        return await db.Users.Where(u => u.UserName == "ama").Select(u => u.Id).SingleAsync();
    }

    [Fact]
    public async Task Nachricht_Ermittelt_Empfaenger_Ohne_Gesperrte_Und_Maskiert_Html()
    {
        var b = await BasisAsync();
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "admin", Email = "admin@exemple.com", FullName = "Admin" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), UserName = "awa", Email = "awa@exemple.com", FullName = "Awa Koffi", Vorname = "Awa" };
        var gesperrt = new AppUser { Id = Guid.NewGuid(), UserName = "kofi", Email = "kofi@exemple.com", FullName = "Kofi", IsBlocked = true };
        var po = new AppUser { Id = Guid.NewGuid(), UserName = "pamela", Email = "po@exemple.com", FullName = "Pamela PO", Vorname = "Pamela" };
        await using (var db = await b.F.CreateDbContextAsync())
        {
            var rolle = new Role { Name = AppRollen.SuperAdmin, NormalizedName = "SUPERADMIN" };
            var poRolle = new Role { Name = AppRollen.ProduktOwner, NormalizedName = "PRODUKTOWNER" };
            db.Roles.AddRange(rolle, poRolle);
            db.Users.AddRange(admin, kassierer, gesperrt, po);
            db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = rolle.Id });
            db.UserRoles.Add(new AppUserRole { UserId = po.Id, RoleId = poRolle.Id });
            db.UserSalons.AddRange(new UserSalon { AppUserId = kassierer.Id, SalonId = b.Salon.Id }, new UserSalon { AppUserId = gesperrt.Id, SalonId = b.Salon.Id });
            await db.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var service = new NachrichtenService(b.F, new EmailService(config, b.F, NullLogger<EmailService>.Instance));

        var besitzer = await service.ErmittleAsync(admin.Id, NachrichtenService.Gruppe.AlleBesitzer, [], []);
        Assert.Equal(["ama@exemple.com"], besitzer.Select(e => e.Email));

        var besitzerUndPo = await service.ErmittleAsync(admin.Id, NachrichtenService.Gruppe.AlleBesitzerUndProduktOwner, [], []);
        Assert.Equal(["ama@exemple.com", "po@exemple.com"], besitzerUndPo.Select(e => e.Email).Order());
        Assert.Equal(["po@exemple.com"], (await service.ErmittleAsync(admin.Id, NachrichtenService.Gruppe.AlleProduktOwner, [], [])).Select(e => e.Email));

        var team = await service.ErmittleAsync(admin.Id, NachrichtenService.Gruppe.TeamVonLaeden, [], [b.Salon.Id]);
        Assert.Equal(["Ama Mensah", "Awa Koffi"], team.Select(e => e.Name).Order());   // Gesperrter fehlt

        Assert.Equal(2, (await service.SucheAsync(admin.Id, "Salon Test")).Count);    // Suche auch über den Laden
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ErmittleAsync(kassierer.Id, NachrichtenService.Gruppe.AlleBesitzer, [], []));

        var (ok, fehler) = await service.SendeAsync(admin.Id, team, "Info", "Ligne 1\n<b>gras</b>\n\nParagraphe 2", null, null);
        Assert.Equal((0, 2), (ok, fehler));                            // ohne Konfiguration: in der Queue
        await using var db2 = await b.F.CreateDbContextAsync();
        var mails = await db2.MailNachrichten.Where(m => m.Kategorie == "message").ToListAsync();
        Assert.Equal(2, mails.Count);
        Assert.All(mails, m => Assert.Contains("&lt;b&gt;gras&lt;/b&gt;", m.HtmlInhalt));
        Assert.Contains(mails, m => m.HtmlInhalt.Contains("Bonjour Awa,"));
    }
}
