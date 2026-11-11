using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Katalog;
using ENote.Core.DataModels.Salons;
using ENote.Core.Services;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ENote.Tests;

/// <summary>Abonnement kündigen (zum Periodenende, zurücknehmbar) und Konto löschen (30 Tage, nur ohne offene Rechnung).</summary>
public class KuendigungTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private const string Passwort = "Initial123!Ab";
    private static readonly DateTime November = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, ServiceProvider Dienste, Uhr Uhr, Guid SalonId, Guid Besitzer, Guid Kassierer)
    {
        public KuendigungService Service => new(F, Uhr, Dienste.GetRequiredService<UserManager<AppUser>>());
        public KassenZugangService Zugang => new(F, Uhr);
        public VertragsService Vertrag => new(F, Uhr);
    }

    private static async Task<Welt> BasisAsync(DateTime jetzt, DateTime? probeBis = null, DateTime? vertragsBeginn = null)
    {
        var f = TestDbContextFactory.Create();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(f);
        services.AddTransient(_ => f.CreateDbContext());
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddIdentityCore<AppUser>().AddRoles<Role>().AddEntityFrameworkStores<ApplicationDbContext>();
        var sp = services.BuildServiceProvider();
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var rollen = sp.GetRequiredService<RoleManager<Role>>();
        foreach (var r in new[] { AppRollen.Besitzer, AppRollen.Kassierer }) await rollen.CreateAsync(new Role { Name = r });

        var besitzer = new AppUser { UserName = "ama.proprio", Email = "ama@exemple.com", FullName = "Ama Mensah", Vorname = "Ama", Nachname = "Mensah", TelefonNummer = "+228 90 00 00 01" };
        var kassierer = new AppUser { UserName = "awa.employee", Email = "awa@exemple.com", FullName = "Awa Koffi" };
        Assert.True((await users.CreateAsync(besitzer, Passwort)).Succeeded);
        Assert.True((await users.CreateAsync(kassierer, Passwort)).Succeeded);
        await users.AddToRoleAsync(besitzer, AppRollen.Besitzer);
        await users.AddToRoleAsync(kassierer, AppRollen.Kassierer);

        var salon = new Salon
        {
            Name = "Salon Élégance", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 4000,
            ProbeBisUtc = probeBis, VertragsBeginnUtc = vertragsBeginn, VertragsIntervall = AboIntervall.Monatlich,
            TelefonNummer = "+228 22 00 00 00", Adresse = "Lomé"
        };
        await using (var db = await f.CreateDbContextAsync())
        {
            db.Salons.Add(salon);
            db.UserSalons.AddRange(new UserSalon { AppUserId = besitzer.Id, SalonId = salon.Id }, new UserSalon { AppUserId = kassierer.Id, SalonId = salon.Id });
            await db.SaveChangesAsync();
        }
        return new Welt(f, sp, new Uhr(jetzt), salon.Id, besitzer.Id, kassierer.Id);
    }

    private static async Task BezahleAlleAsync(Welt w)
    {
        await using var db = await w.F.CreateDbContextAsync();
        foreach (var a in await db.NutzungsAbos.ToListAsync()) a.ZahlungStatus = AboZahlungStatus.Bestaetigt;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Kuendigung_Wirkt_Zum_Periodenende_Danach_Keine_Rechnung_Und_Kasse_Zu()
    {
        var w = await BasisAsync(November.AddDays(10), vertragsBeginn: November);
        await w.Vertrag.SynchronisiereAsync(w.SalonId);
        await BezahleAlleAsync(w);

        var stand = await w.Service.GetStandAsync(w.Besitzer, w.SalonId);
        Assert.Equal(November.AddMonths(1), stand.WuerdeWirkenZumUtc);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => w.Service.KuendigeAsync(w.Kassierer, w.SalonId, null));

        var zum = await w.Service.KuendigeAsync(w.Besitzer, w.SalonId, "Trop cher");
        Assert.Equal(November.AddMonths(1), zum);
        var vorher = await w.Zugang.PruefeAsync(w.SalonId);
        Assert.False(vorher.Gesperrt);
        Assert.Contains("résilié", vorher.Hinweis);
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.KuendigeAsync(w.Besitzer, w.SalonId, null));

        w.Uhr.Jetzt = November.AddMonths(2).AddDays(3);
        await w.Vertrag.SynchronisiereAsync(w.SalonId);
        await using (var db = await w.F.CreateDbContextAsync())
            Assert.Equal(1, await db.NutzungsAbos.CountAsync());                    // keine Dezember-/Januar-Rechnung
        var danach = await w.Zugang.PruefeAsync(w.SalonId);
        Assert.True(danach.Gesperrt);
        Assert.Contains("Abonnement résilié", danach.Hinweis);

        // Reaktivieren: Abrechnung beginnt heute, die Pause wird nicht berechnet
        await w.Service.NimmKuendigungZurueckAsync(w.Besitzer, w.SalonId);
        await w.Vertrag.SynchronisiereAsync(w.SalonId);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var perioden = await db.NutzungsAbos.OrderBy(a => a.ZeitraumVonUtc).ToListAsync();
            Assert.Equal(2, perioden.Count);
            Assert.Equal(w.Uhr.Jetzt, perioden[1].ZeitraumVonUtc);
            Assert.True(perioden[1].IstTeilperiode);
            var salon = await db.Salons.SingleAsync();
            Assert.Null(salon.GekuendigtZumUtc);
            Assert.Null(salon.KuendigungGrund);
        }
        Assert.False((await w.Zugang.PruefeAsync(w.SalonId)).Gesperrt);
    }

    [Fact]
    public async Task Kuendigung_In_Der_Probezeit_Endet_Mit_Der_Probe()
    {
        var probe = November.AddDays(20).AddHours(15);
        var w = await BasisAsync(November.AddDays(5), probeBis: probe);
        var zum = await w.Service.KuendigeAsync(w.Besitzer, w.SalonId, null);
        Assert.Equal(VertragsRegeln.BeginnNachProbe(probe), zum);

        w.Uhr.Jetzt = November.AddMonths(1).AddDays(2);
        await w.Vertrag.SynchronisiereAsync(w.SalonId);
        await using (var db = await w.F.CreateDbContextAsync())
            Assert.False(await db.NutzungsAbos.AnyAsync());                        // nach der Probe keine Rechnung
        Assert.True((await w.Zugang.PruefeAsync(w.SalonId)).Gesperrt);
    }

    [Fact]
    public async Task Loeschung_Nur_Ohne_Offene_Rechnung_Mit_Passwort_Und_Zuruecknehmbar()
    {
        var w = await BasisAsync(November.AddDays(10), vertragsBeginn: November);
        await w.Vertrag.SynchronisiereAsync(w.SalonId);

        var offen = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Service.BeantrageLoeschungAsync(w.Besitzer, w.SalonId, Passwort));
        Assert.Contains("4.000", offen.Message.Replace(" ", ".").Replace(" ", ".").Replace(",", "."));
        Assert.False((await w.Service.GetStandAsync(w.Besitzer, w.SalonId)).LoeschungMoeglich);

        await BezahleAlleAsync(w);
        await Assert.ThrowsAsync<ArgumentException>(() => w.Service.BeantrageLoeschungAsync(w.Besitzer, w.SalonId, "faux"));
        var ab = await w.Service.BeantrageLoeschungAsync(w.Besitzer, w.SalonId, Passwort);
        Assert.Equal(w.Uhr.Jetzt.AddDays(KuendigungsRegeln.LoeschFristTage), ab);
        var zugang = await w.Zugang.PruefeAsync(w.SalonId);
        Assert.True(zugang.Gesperrt);
        Assert.Contains("Suppression du compte demandée", zugang.Hinweis);

        // Bedenkzeit: zurücknehmen, Kasse wieder offen, nichts gelöscht
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddDays(5);
        Assert.Equal(0, await w.Service.FuehreFaelligeLoeschungenAusAsync());
        await w.Service.NimmLoeschungZurueckAsync(w.Besitzer, w.SalonId);
        Assert.False((await w.Zugang.PruefeAsync(w.SalonId)).Gesperrt);
        await using var db = await w.F.CreateDbContextAsync();
        var salon = await db.Salons.SingleAsync();
        Assert.Null(salon.LoeschungBeantragtUtc);
        Assert.Null(salon.GeloeschtUtc);
    }

    [Fact]
    public async Task Nach_30_Tagen_Werden_Daten_Geloescht_Konten_Anonymisiert_Rechnungen_Bleiben()
    {
        var w = await BasisAsync(November.AddDays(10), vertragsBeginn: November);
        await w.Vertrag.SynchronisiereAsync(w.SalonId);
        await BezahleAlleAsync(w);

        // Ladendaten: Kunde mit Notiz und Besuch, Leistung, Kassensitzung mit Verkauf
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var kunde = new Kunde { SalonId = w.SalonId, Name = "Akossiwa", Telefon = "+228 90 12 34 56", TelefonZiffern = "22890123456" };
            var besuch = new KundenBesuch { SalonId = w.SalonId, Kunde = kunde, Grund = "Coupe", ErfasstVonName = "Awa" };
            db.KundenNotizen.Add(new KundenNotiz { SalonId = w.SalonId, Kunde = kunde, BesuchId = besuch.Id, Text = "Allergie", ErstelltVonName = "Awa" });
            db.KundenBesuche.Add(besuch);
            var leistung = new Leistung { SalonId = w.SalonId, Name = "Coupe", Preis = 3000, IstAktiv = true };
            var sitzung = new KassenSitzung { SalonId = w.SalonId, KassiererId = w.Kassierer, Status = KassenSitzungStatus.Abgeschlossen };
            var verkauf = new Verkauf { KassenSitzung = sitzung, SalonId = w.SalonId, Kunde = kunde, GesamtSumme = 3000 };
            verkauf.Positionen.Add(new VerkaufsPosition { Leistung = leistung, LeistungName = "Coupe", Menge = 1, ZeilenSumme = 3000 });
            db.AddRange(leistung, sitzung, verkauf);
            db.MailNachrichten.Add(new Core.DataModels.Infrastruktur.MailNachricht { Empfaenger = "ama@exemple.com", Betreff = "Facture", HtmlInhalt = "<p>Ama</p>", Kategorie = "facture" });
            await db.SaveChangesAsync();
        }

        await w.Service.BeantrageLoeschungAsync(w.Besitzer, w.SalonId, Passwort);
        w.Uhr.Jetzt = w.Uhr.Jetzt.AddDays(KuendigungsRegeln.LoeschFristTage).AddHours(1);
        Assert.Equal(1, await w.Service.FuehreFaelligeLoeschungenAusAsync());
        Assert.Equal(0, await w.Service.FuehreFaelligeLoeschungenAusAsync());       // idempotent

        await using var pruef = await w.F.CreateDbContextAsync();
        Assert.False(await pruef.Kunden.AnyAsync());
        Assert.False(await pruef.KundenNotizen.AnyAsync());
        Assert.False(await pruef.KundenBesuche.AnyAsync());
        Assert.False(await pruef.Verkaeufe.AnyAsync());
        Assert.False(await pruef.VerkaufsPositionen.AnyAsync());
        Assert.False(await pruef.KassenSitzungen.AnyAsync());
        Assert.False(await pruef.Leistungen.AnyAsync());
        Assert.False(await pruef.UserSalons.AnyAsync());
        Assert.False(await pruef.MailNachrichten.AnyAsync(m => m.Empfaenger == "ama@exemple.com"));
        Assert.Equal(1, await pruef.NutzungsAbos.CountAsync());                     // Rechnung bleibt

        var salon = await pruef.Salons.SingleAsync();
        Assert.NotNull(salon.GeloeschtUtc);
        Assert.False(salon.IstAktiv);
        Assert.StartsWith("Établissement supprimé", salon.Name);
        Assert.Null(salon.TelefonNummer);
        Assert.Null(salon.Adresse);

        foreach (var konto in await pruef.Users.ToListAsync())
        {
            Assert.StartsWith("supprime-", konto.UserName);
            Assert.Null(konto.Email);
            Assert.Null(konto.PasswordHash);
            Assert.Null(konto.TelefonNummer);
            Assert.Equal("Compte supprimé", konto.FullName);
            Assert.True(konto.IsBlocked);
        }
    }
}
