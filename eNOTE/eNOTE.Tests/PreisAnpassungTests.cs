using System.Security.Claims;
using ENote.Core.DataModels.Abo;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Infrastruktur;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Tarifänderung für alle: Standard ist Mindestpreis, Erhöhung wirkt auf alle Preise (auch individuelle),
/// Senkung lässt individuelle Preise stehen; neue Perioden ab dem Datum nutzen den neuen Preis.
/// </summary>
public class PreisAnpassungTests
{
    private sealed class Uhr(DateTime jetzt) : TimeProvider
    {
        public DateTime Jetzt { get; set; } = jetzt;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Jetzt, DateTimeKind.Utc));
    }

    private static readonly DateTime Heute = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dezember = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Welt(IDbContextFactory<ApplicationDbContext> F, Guid Admin, Salon Standard, Salon Eigener, Salon Alt);

    private static async Task<Welt> BasisAsync(decimal standard = 3600)
    {
        var f = TestDbContextFactory.Create();
        var admin = new AppUser { Id = Guid.NewGuid(), FullName = "Admin" };
        var a = new Salon { Name = "A Standard", IstAktiv = true, IstFreigegeben = true, VertragsBeginnUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc) };
        var b = new Salon { Name = "B Eigener", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 5000, VertragsBeginnUtc = a.VertragsBeginnUtc };
        // Altlast: individueller Preis unter dem Standard (vor der Mindestpreis-Regel vergeben)
        var c = new Salon { Name = "C Alt", IstAktiv = true, IstFreigegeben = true, IndividuellerMonatsPreis = 3000, VertragsBeginnUtc = a.VertragsBeginnUtc };
        await using var db = await f.CreateDbContextAsync();
        var rolle = new Role { Name = AppRollen.SuperAdmin };
        db.Roles.Add(rolle);
        db.Users.Add(admin);
        db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = rolle.Id });
        db.Salons.AddRange(a, b, c);
        db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = null, MonatsPreis = standard, GueltigAbUtc = Heute.AddMonths(-3) });
        db.MonatsPreisStaende.Add(new MonatsPreisStand { SalonId = b.Id, MonatsPreis = 5000, GueltigAbUtc = Heute.AddMonths(-2) });
        await db.SaveChangesAsync();
        return new Welt(f, admin.Id, a, b, c);
    }

    [Fact]
    public void Regel_Erhoehung_Gilt_Fuer_Alle_Senkung_Nie_Unter_Standard()
    {
        Assert.Equal(5500m, PreisAnpassungService.NeuerIndividuellerPreis(5000, 3600, 4100));
        Assert.Null(PreisAnpassungService.NeuerIndividuellerPreis(3000, 3600, 4100));   // 3.500 < 4.100 → Standard
        Assert.Null(PreisAnpassungService.NeuerIndividuellerPreis(null, 3600, 4100));
        Assert.Equal(5000m, PreisAnpassungService.NeuerIndividuellerPreis(5000, 5000, 3600)); // Senkung: bleibt
        Assert.Null(PreisAnpassungService.NeuerIndividuellerPreis(3500, 5000, 3600));          // fällt nie darunter
    }

    [Fact]
    public async Task Erhoehung_Ab_Dezember_Erhoeht_Alle_Preise_Und_Neue_Perioden_Nutzen_Sie()
    {
        var w = await BasisAsync();
        var uhr = new Uhr(Heute);
        var service = new PreisAnpassungService(w.F, uhr);

        var vorschau = await service.GetVorschauAsync(w.Admin, 4100, Dezember);
        Assert.Equal(500m, vorschau.Differenz);
        Assert.Equal(4100m, vorschau.Laeden.Single(l => l.SalonId == w.Standard.Id).Neu);
        Assert.Equal(5500m, vorschau.Laeden.Single(l => l.SalonId == w.Eigener.Id).Neu);
        Assert.Equal(4100m, vorschau.Laeden.Single(l => l.SalonId == w.Alt.Id).Neu);

        var ergebnis = await service.PlaneAsync(w.Admin, 4100, Dezember, perMail: false);
        Assert.Equal(Dezember, ergebnis.AbUtc);
        Assert.Equal(2, ergebnis.AngepassteLaeden);

        // Vor dem Datum gilt noch der alte Preis …
        await using (var db = await w.F.CreateDbContextAsync())
        {
            Assert.Equal(3600m, await AboService.GlobalerMonatsPreisAsync(db));
            Assert.Equal((Dezember, 5500m), await PreisAnpassungService.KommenderPreisAsync(db, w.Eigener.Id, Heute, default));
            Assert.Equal((Dezember, 4100m), await PreisAnpassungService.KommenderPreisAsync(db, w.Standard.Id, Heute, default));
            var banner = await db.Ankuendigungen.SingleAsync();
            Assert.Equal(AnkuendigungArt.Tarif, banner.Art);
            Assert.Equal(AnkuendigungZiel.Proprietaires, banner.Ziel);
            Assert.Contains("4 100", banner.Text.Replace(' ', ' ').Replace(' ', ' '));
        }

        // … die Dezember-Periode wird mit dem neuen Preis erzeugt.
        uhr.Jetzt = Dezember.AddDays(1);
        var vertrag = new VertragsService(w.F, uhr);
        foreach (var salon in new[] { w.Standard, w.Eigener, w.Alt }) await vertrag.SynchronisiereAsync(salon.Id);
        await using (var db = await w.F.CreateDbContextAsync())
        {
            var dezember = await db.NutzungsAbos.Where(a => a.ZeitraumVonUtc == Dezember).ToDictionaryAsync(a => a.SalonId);
            Assert.Equal(4100m, dezember[w.Standard.Id].PreisSnapshot);
            Assert.Equal(5500m, dezember[w.Eigener.Id].PreisSnapshot);
            Assert.Equal(4100m, dezember[w.Alt.Id].PreisSnapshot);
            var november = await db.NutzungsAbos.SingleAsync(a => a.SalonId == w.Eigener.Id && a.ZeitraumVonUtc.Month == 11);
            Assert.Equal(5000m, november.PreisSnapshot); // bereits erzeugte Periode bleibt
        }

        // Hintergrunddienst übernimmt die Werte in die Anzeige-Felder.
        Assert.Equal(1, await service.WendeFaelligeAnAsync());
        await using (var db = await w.F.CreateDbContextAsync())
        {
            Assert.Equal(5500m, (await db.Salons.SingleAsync(s => s.Id == w.Eigener.Id)).IndividuellerMonatsPreis);
            Assert.Null((await db.Salons.SingleAsync(s => s.Id == w.Alt.Id)).IndividuellerMonatsPreis);
            Assert.Equal(4100m, (await db.PlattformPreise.SingleAsync(p => p.IstAktuell && p.Intervall == AboIntervall.Monatlich)).Preis);
        }
        Assert.Equal(0, await service.WendeFaelligeAnAsync());
    }

    [Fact]
    public async Task Erhoehung_Muss_Angekuendigt_Werden_Senkung_Darf_Sofort()
    {
        var w = await BasisAsync(standard: 5000);
        var service = new PreisAnpassungService(w.F, new Uhr(Heute));

        await Assert.ThrowsAsync<ArgumentException>(() => service.PlaneAsync(w.Admin, 5500, null, false));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlaneAsync(w.Admin, 5500, Dezember.AddDays(3), false));

        // Startpreis 5.000 → 3.600 sofort: individueller Preis 5.000 bleibt, 3.000 fällt auf den Standard.
        var ergebnis = await service.PlaneAsync(w.Admin, 3600, null, false);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal(3600m, await AboService.GlobalerMonatsPreisAsync(db));
        Assert.Equal(3600m, (await db.PlattformPreise.SingleAsync(p => p.IstAktuell && p.Intervall == AboIntervall.Monatlich)).Preis);
        Assert.Equal(5000m, (await db.Salons.SingleAsync(s => s.Id == w.Eigener.Id)).IndividuellerMonatsPreis);
        Assert.Null((await db.Salons.SingleAsync(s => s.Id == w.Alt.Id)).IndividuellerMonatsPreis);
        Assert.Equal(1, ergebnis.AngepassteLaeden);
        Assert.True((await db.PreisAnpassungen.SingleAsync()).IstAngewendet);
    }

    [Fact]
    public async Task Nur_Eine_Geplante_Aenderung_Und_Aufheben_Entfernt_Alles()
    {
        var w = await BasisAsync();
        var service = new PreisAnpassungService(w.F, new Uhr(Heute));
        var ergebnis = await service.PlaneAsync(w.Admin, 4100, Dezember, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PlaneAsync(w.Admin, 4600, Dezember.AddMonths(1), false));

        var aufhebung = await service.AufhebenAsync(w.Admin, ergebnis.Id);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.False(await db.MonatsPreisStaende.AnyAsync(p => p.PreisAnpassungId == ergebnis.Id));
        var anpassung = await db.PreisAnpassungen.SingleAsync();
        Assert.False((await db.Ankuendigungen.SingleAsync(a => a.Id == anpassung.AnkuendigungId)).IstAktiv);
        // Entwarnung statt nur Banner ausblenden (W1): neues Tarif-Banner, ohne Mail, weil die Ankündigung keine hatte.
        var entwarnung = await db.Ankuendigungen.SingleAsync(a => a.Id == aufhebung.EntwarnungId);
        Assert.True(entwarnung.IstAktiv);
        Assert.Equal(PreisAnpassungService.EntwarnungErhoehung.Titel, entwarnung.Titel);
        Assert.Equal(0, aufhebung.Mails);
        Assert.Equal((true, Heute, (Guid?)w.Admin), (anpassung.IstAufgehoben, anpassung.AufgehobenUtc!.Value, anpassung.AufgehobenVonId));
        Assert.Null(await PreisAnpassungService.KommenderPreisAsync(db, w.Eigener.Id, Heute, default));
        var stand = await service.GetStandAsync(w.Admin);
        Assert.Null(stand.Geplant);
        Assert.True(Assert.Single(stand.Verlauf).IstAufgehoben); // Journal: aufgehobene Änderung bleibt im Verlauf sichtbar
    }

    /// <summary>
    /// W1: Wird eine angekündigte Erhöhung aufgehoben, bekommen genau die Besitzer, die die Ankündigung per Mail
    /// bekamen, eine Entwarnung (bearbeitbarer Text, « vous »); das Banner sehen nur Konten, die es damals schon gab.
    /// </summary>
    [Fact]
    public async Task Aufheben_Gibt_Entwarnung_An_Die_Empfaenger_Der_Ankuendigung()
    {
        var w = await BasisAsync();
        var uhr = new Uhr(Heute);
        var besitzerRolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
        AppUser Besitzer(string vorname, string mail) => new() { Id = Guid.NewGuid(), Vorname = vorname, FullName = vorname, UserName = vorname.ToLowerInvariant(), Email = mail, CreatedAtUtc = Heute.AddMonths(-1) };
        var ama = Besitzer("Ama", "ama@exemple.com");
        var kofi = Besitzer("Kofi", "kofi@exemple.com");
        var gesperrt = Besitzer("Esi", "esi@exemple.com");
        gesperrt.IsBlocked = true;
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Roles.Add(besitzerRolle);
            db.Users.AddRange(ama, kofi, gesperrt);
            foreach (var (konto, salon) in new[] { (ama, w.Standard), (kofi, w.Eigener), (gesperrt, w.Alt) })
            {
                db.UserRoles.Add(new AppUserRole { UserId = konto.Id, RoleId = besitzerRolle.Id });
                db.UserSalons.Add(new UserSalon { AppUserId = konto.Id, SalonId = salon.Id });
            }
            foreach (var s in await db.Salons.ToListAsync()) s.CreatedAtUtc = Heute.AddMonths(-1);
            await db.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new ENote.WebApp.Services.Mails.EmailService(config, w.F, NullLogger<ENote.WebApp.Services.Mails.EmailService>.Instance);
        var service = new PreisAnpassungService(w.F, uhr, email);
        var geplant = await service.PlaneAsync(w.Admin, 4100, Dezember, perMail: true);

        // Nach der Ankündigung kommt ein neuer Laden dazu – sein Besitzer bekam keine Ankündigung.
        uhr.Jetzt = Heute.AddDays(2);
        var neu = Besitzer("Yao", "yao@exemple.com");
        neu.CreatedAtUtc = uhr.Jetzt;
        var neuerLaden = new Salon { Name = "D Neu", IstAktiv = true, IstFreigegeben = true, CreatedAtUtc = uhr.Jetzt };
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Users.Add(neu);
            db.Salons.Add(neuerLaden);
            db.UserRoles.Add(new AppUserRole { UserId = neu.Id, RoleId = besitzerRolle.Id });
            db.UserSalons.Add(new UserSalon { AppUserId = neu.Id, SalonId = neuerLaden.Id });
            await db.SaveChangesAsync();
        }

        var vorschau = await service.GetAufhebungsVorschauAsync(w.Admin, geplant.Id);
        Assert.True(vorschau.AnkuendigungPerMail);
        Assert.Equal((2, 2), (vorschau.AnkuendigungsMails, vorschau.MailEmpfaenger)); // Ama + Kofi, nicht der gesperrte Esi
        Assert.Contains("n'aura pas lieu", vorschau.Vorlage.Titel);

        uhr.Jetzt = Heute.AddDays(3);
        var ergebnis = await service.AufhebenAsync(w.Admin, geplant.Id,
            new PreisAnpassungService.EntwarnungsText(vorschau.Vorlage.Titel, "Bonne nouvelle : {nouveau} n'aura pas lieu, vous restez à {ancien}.", PerMail: true));

        await using (var db = await w.F.CreateDbContextAsync())
        {
            var entwarnungen = await db.MailNachrichten.Where(m => m.Kategorie == PreisAnpassungService.KategorieEntwarnung).ToListAsync();
            Assert.Equal(new[] { "ama@exemple.com", "kofi@exemple.com" }, entwarnungen.Select(m => m.Empfaenger).Order().ToArray());
            Assert.All(entwarnungen, m => Assert.Contains("n'aura pas lieu", m.Betreff));
            Assert.Contains("Bonjour Ama", entwarnungen.Single(m => m.Empfaenger == "ama@exemple.com").HtmlInhalt);
            var banner = await db.Ankuendigungen.SingleAsync(a => a.Id == ergebnis.EntwarnungId);
            Assert.Equal((AnkuendigungArt.Tarif, AnkuendigungZiel.Proprietaires, true), (banner.Art, banner.Ziel, banner.IstAktiv));
            Assert.Equal("Bonne nouvelle : 4 100 F n'aura pas lieu, vous restez à 3 600 F.", banner.Text.Replace(" ", " ").Replace(" ", " "));
            Assert.Equal(Dezember, banner.SichtbarBisUtc);
        }

        // Banner: Ama sieht die Entwarnung (nicht mehr die Ankündigung), ein danach angelegtes Konto nicht.
        var ankuendigungen = new AnkuendigungService(w.F, uhr);
        static ClaimsPrincipal Rolle() => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRollen.Besitzer)], "test"));
        Assert.Equal(ergebnis.EntwarnungId, Assert.Single(await ankuendigungen.GetFuerNutzerAsync(ama.Id, Rolle())).Id);
        var spaeter = Besitzer("Afi", "afi@exemple.com");
        spaeter.CreatedAtUtc = Heute.AddDays(4);
        await using (var db = await w.F.CreateDbContextAsync()) { db.Users.Add(spaeter); await db.SaveChangesAsync(); }
        Assert.Empty(await ankuendigungen.GetFuerNutzerAsync(spaeter.Id, Rolle()));

        // Zweites Aufheben ist harmlos, nichts doppelt.
        await service.AufhebenAsync(w.Admin, geplant.Id);
        await using (var db = await w.F.CreateDbContextAsync())
            Assert.Equal(2, await db.MailNachrichten.CountAsync(m => m.Kategorie == PreisAnpassungService.KategorieEntwarnung));
    }

    /// <summary>Klickprüfung W1: Ankündigen, ohne Mail aufheben, gleich wieder ankündigen (gleicher Titel) – die Entwarnung
    /// geht nur an die Empfänger der letzten Ankündigung, nicht dreifach.</summary>
    [Fact]
    public async Task Entwarnung_Zaehlt_Nur_Die_Mails_Der_Aufgehobenen_Ankuendigung()
    {
        var w = await BasisAsync();
        var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
        var ama = new AppUser { Id = Guid.NewGuid(), Vorname = "Ama", FullName = "Ama", UserName = "ama", Email = "ama@exemple.com" };
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Roles.Add(rolle);
            db.Users.Add(ama);
            db.UserRoles.Add(new AppUserRole { UserId = ama.Id, RoleId = rolle.Id });
            db.UserSalons.Add(new UserSalon { AppUserId = ama.Id, SalonId = w.Standard.Id });
            await db.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new ENote.WebApp.Services.Mails.EmailService(config, w.F, NullLogger<ENote.WebApp.Services.Mails.EmailService>.Instance);
        var service = new PreisAnpassungService(w.F, TimeProvider.System, email);
        var jetzt = DateTime.UtcNow;
        var ab = new DateTime(jetzt.Year, jetzt.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(2);

        var erste = await service.PlaneAsync(w.Admin, 4100, ab, perMail: true);
        await service.AufhebenAsync(w.Admin, erste.Id, new PreisAnpassungService.EntwarnungsText("", "", PerMail: false));
        await Task.Delay(20);
        var zweite = await service.PlaneAsync(w.Admin, 4100, ab, perMail: true);
        Assert.Equal(1, (await service.GetAufhebungsVorschauAsync(w.Admin, zweite.Id)).AnkuendigungsMails);
        await service.AufhebenAsync(w.Admin, zweite.Id);
        await using var pruef = await w.F.CreateDbContextAsync();
        Assert.Equal(2, await pruef.MailNachrichten.CountAsync(m => m.Kategorie == PreisAnpassungService.KategorieAnkuendigung));
        Assert.Equal(1, await pruef.MailNachrichten.CountAsync(m => m.Kategorie == PreisAnpassungService.KategorieEntwarnung));
    }

    [Fact]
    public async Task Aufheben_Einer_Ankuendigung_Ohne_Mail_Schreibt_Nur_Auf_Wunsch_Und_Nur_Alten_Laeden()
    {
        var w = await BasisAsync();
        var uhr = new Uhr(Heute);
        var rolle = new Role { Name = AppRollen.Besitzer, NormalizedName = "BESITZER" };
        var ama = new AppUser { Id = Guid.NewGuid(), Vorname = "Ama", FullName = "Ama", UserName = "ama", Email = "ama@exemple.com" };
        var yao = new AppUser { Id = Guid.NewGuid(), Vorname = "Yao", FullName = "Yao", UserName = "yao", Email = "yao@exemple.com" };
        var neuerLaden = new Salon { Name = "D Neu", IstAktiv = true, IstFreigegeben = true, CreatedAtUtc = Heute.AddDays(1) };
        await using (var db = await w.F.CreateDbContextAsync())
        {
            db.Roles.Add(rolle);
            db.Users.AddRange(ama, yao);
            db.Salons.Add(neuerLaden);
            db.UserRoles.AddRange(new AppUserRole { UserId = ama.Id, RoleId = rolle.Id }, new AppUserRole { UserId = yao.Id, RoleId = rolle.Id });
            db.UserSalons.AddRange(new UserSalon { AppUserId = ama.Id, SalonId = w.Standard.Id }, new UserSalon { AppUserId = yao.Id, SalonId = neuerLaden.Id });
            (await db.Salons.SingleAsync(s => s.Id == w.Standard.Id)).CreatedAtUtc = Heute.AddMonths(-1);
            await db.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var email = new ENote.WebApp.Services.Mails.EmailService(config, w.F, NullLogger<ENote.WebApp.Services.Mails.EmailService>.Instance);
        var service = new PreisAnpassungService(w.F, uhr, email);
        var geplant = await service.PlaneAsync(w.Admin, 4100, Dezember, perMail: false);

        var vorschau = await service.GetAufhebungsVorschauAsync(w.Admin, geplant.Id);
        Assert.False(vorschau.AnkuendigungPerMail);   // Schalter im Dialog standardmäßig aus
        Assert.Equal(1, vorschau.MailEmpfaenger);       // nur Ama: Laden D entstand nach der Ankündigung

        uhr.Jetzt = Heute.AddDays(2);
        await service.AufhebenAsync(w.Admin, geplant.Id, new PreisAnpassungService.EntwarnungsText("Hausse annulée", "Le tarif reste à {ancien}.", PerMail: true));
        await using var pruef = await w.F.CreateDbContextAsync();
        Assert.Equal("ama@exemple.com", (await pruef.MailNachrichten.SingleAsync()).Empfaenger);
        var sofort = await service.PlaneAsync(w.Admin, 3000, null, false); // Senkung sofort ist schon in Kraft
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AufhebenAsync(w.Admin, sofort.Id));
    }

    [Fact]
    public async Task Individueller_Preis_Nicht_Unter_Standard_Und_Geplante_Erhoehung_Gilt_Mit()
    {
        var w = await BasisAsync();
        var admin = new AdminService(w.F, null!, NullLogger<AdminService>.Instance, null!);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => admin.SetzeIndividuellenPreisAsync(w.Standard.Id, 3000));

        // Erhöhung geplant – danach vergebener Preis 7.000 wird im Dezember ebenfalls zu 7.500.
        var service = new PreisAnpassungService(w.F, new Uhr(DateTime.UtcNow));
        var ab = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        await service.PlaneAsync(w.Admin, 4100, ab, false);
        await admin.SetzeIndividuellenPreisAsync(w.Standard.Id, 7000);
        await using var db = await w.F.CreateDbContextAsync();
        Assert.Equal((ab, 7500m), await PreisAnpassungService.KommenderPreisAsync(db, w.Standard.Id, DateTime.UtcNow, default));
    }

    [Fact]
    public async Task Eigener_Text_Mit_Platzhaltern_Wird_Zum_Banner()
    {
        Assert.All(PreisAnpassungService.VorlagenErhoehung.Concat(PreisAnpassungService.VorlagenSenkung),
            v => Assert.DoesNotContain("{", PreisAnpassungService.Fuelle(v.Titel + v.Text, 3600, 4100, Dezember)));
        Assert.StartsWith("À partir du 1er décembre 2026", PreisAnpassungService.Fuelle("{quand}, c'est {nouveau}.", 3600, 4100, Dezember));
        Assert.Contains("dès maintenant", PreisAnpassungService.Fuelle("Tarif {quand}", 5000, 3600, null));

        var w = await BasisAsync();
        var service = new PreisAnpassungService(w.F, new Uhr(Heute));
        await service.PlaneAsync(w.Admin, 4100, Dezember, false, "Hausse de {ecart}", "Dès le {date} : {ancien} → {nouveau}.");
        await using var db = await w.F.CreateDbContextAsync();
        var banner = await db.Ankuendigungen.SingleAsync();
        static string N(string s) => s.Replace(' ', ' ').Replace(' ', ' ');
        Assert.Equal("Hausse de 500 F", N(banner.Titel));
        Assert.Equal("Dès le 1er décembre 2026 : 3 600 F → 4 100 F.", N(banner.Text));
    }

    [Fact]
    public async Task Geplante_Aktualisierung_Mit_Zeitfenster_Verschwindet_Danach_Und_Meldet_Erfolg()
    {
        var w = await BasisAsync();
        var uhr = new Uhr(Heute);
        var service = new AnkuendigungService(w.F, uhr);
        var von = new DateTime(2026, 11, 12, 22, 0, 0, DateTimeKind.Utc);
        var vorlage = AnkuendigungService.WartungsVorlagen[0];
        await service.VeroeffentlicheAsync(w.Admin, new AnkuendigungService.Entwurf(AnkuendigungArt.Maintenance,
            AnkuendigungZiel.ProprietairesEtCaissiers, vorlage.Titel, vorlage.Text, null, null, null, false,
            von, von.AddMinutes(30), HinweisDanach: true));

        static ClaimsPrincipal Kassierer() => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRollen.Kassierer)], "test"));
        var nutzer = Guid.NewGuid();

        // Vorher: Banner mit ausgefülltem Zeitfenster, noch nicht „en cours“
        var vorher = Assert.Single(await service.GetFuerNutzerAsync(nutzer, Kassierer()));
        Assert.Equal("Mise à jour d'eNOTE le jeudi 12 novembre, de 22 h 00 à 22 h 30", vorher.Titel);
        Assert.Contains("avant 22 h 00", vorher.Text);
        Assert.False(vorher.WartungLaeuft(uhr.Jetzt));

        uhr.Jetzt = von.AddMinutes(10);
        Assert.True(Assert.Single(await service.GetFuerNutzerAsync(nutzer, Kassierer())).WartungLaeuft(uhr.Jetzt));

        // Danach: Wartungs-Banner weg, kurzer Hinweis „eNOTE a été mis à jour“
        uhr.Jetzt = von.AddMinutes(31);
        var danach = Assert.Single(await service.GetFuerNutzerAsync(nutzer, Kassierer()));
        Assert.Equal(AnkuendigungService.TitelDanach, danach.Titel);
        uhr.Jetzt = von.AddDays(4);
        Assert.Empty(await service.GetFuerNutzerAsync(nutzer, Kassierer()));

        // Ungültige Fenster
        uhr.Jetzt = Heute;
        await Assert.ThrowsAsync<ArgumentException>(() => service.VeroeffentlicheAsync(w.Admin, new AnkuendigungService.Entwurf(
            AnkuendigungArt.Maintenance, AnkuendigungZiel.Tous, "T", "X", null, null, null, false, von, von.AddMinutes(-5))));
        await Assert.ThrowsAsync<ArgumentException>(() => service.VeroeffentlicheAsync(w.Admin, new AnkuendigungService.Entwurf(
            AnkuendigungArt.Maintenance, AnkuendigungZiel.Tous, "T", "X", null, null, null, false)));
        Assert.Equal("1 h 30", AnkuendigungService.DauerText(TimeSpan.FromMinutes(90)));
    }

    [Fact]
    public async Task Ankuendigung_Erscheint_Nur_Fuer_Zielgruppe_Bis_Sie_Geschlossen_Wird()
    {
        var w = await BasisAsync();
        var uhr = new Uhr(Heute);
        var service = new AnkuendigungService(w.F, uhr);
        await service.VeroeffentlicheAsync(w.Admin, new AnkuendigungService.Entwurf(AnkuendigungArt.Nouveaute,
            AnkuendigungZiel.Proprietaires, "Nouvelle version", "Remboursements et mode sombre.", "/ventes", null, null, false));

        static ClaimsPrincipal Mit(string rolle) => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "test"));
        var besitzer = Guid.NewGuid();
        Assert.Single(await service.GetFuerNutzerAsync(besitzer, Mit(AppRollen.Besitzer)));
        Assert.Empty(await service.GetFuerNutzerAsync(Guid.NewGuid(), Mit(AppRollen.Kassierer)));

        var banner = (await service.GetFuerNutzerAsync(besitzer, Mit(AppRollen.Besitzer))).Single();
        Assert.Equal("En savoir plus", banner.LinkText);
        await service.MarkiereGelesenAsync(besitzer, banner.Id);
        await service.MarkiereGelesenAsync(besitzer, banner.Id); // zweimal schließen ist harmlos
        Assert.Empty(await service.GetFuerNutzerAsync(besitzer, Mit(AppRollen.Besitzer)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.VeroeffentlicheAsync(w.Admin, new AnkuendigungService.Entwurf(
            AnkuendigungArt.Information, AnkuendigungZiel.Tous, "Titre", "Texte", "javascript:alert(1)", null, null, false)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.VeroeffentlicheAsync(besitzer, new AnkuendigungService.Entwurf(
            AnkuendigungArt.Information, AnkuendigungZiel.Tous, "Titre", "Texte", null, null, null, false)));
    }
}
