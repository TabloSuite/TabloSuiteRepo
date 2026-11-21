using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Organisation;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Selbstregistrierung: wartende Zustände, doppelte Namen, Bremse, Nutzungsbedingungen, Übergabe an die Direktion (E3).</summary>
public class RegistrierungTests
{
    [Fact]
    public async Task Direction_legt_wartende_Schule_und_Direktion_an_und_informiert_Admins()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();

        var ergebnis = await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction(), "10.0.0.1");

        Assert.Equal("kossi.mensah", ergebnis.Identifiant);
        var (schule, konto) = await u.LiesAsync(async db =>
        {
            var s = await db.Schulen.SingleAsync(x => x.Name == "Collège Horizon");
            return (s, await db.Users.SingleAsync(k => k.SchuleId == s.Id));
        });
        Assert.Equal(SchulStatus.EnAttente, schule.Status);
        Assert.Equal("college horizon", schule.NameNormalisiert);
        // Anmeldedatum nach der Uhr der App (Abrechnung ab Anmeldung, 21.11.2026) – hier die Testuhr, nicht die echte Zeit.
        Assert.Equal(u.Zeit.GetUtcNow().UtcDateTime, schule.ErstelltAmUtc);
        Assert.Equal((Rollen.Direktor, KontoStatus.EnAttente, true, false), (konto.Rolle, konto.Status, konto.SelbstRegistriert, konto.MussPasswortAendern));
        Assert.NotNull(konto.AgbAkzeptiertAmUtc);
        Assert.Equal(AnmeldeErgebnis.Warteseite, AnmeldeRegeln.Pruefe(konto.Status, konto.Rolle, schule.Status));
        Assert.Single(await u.MailsAsync("dir@exemple.tg"));
        var admin = Assert.Single(await u.MailsAsync("admin@exemple.tg"));
        Assert.Contains("Collège Horizon", admin.Betreff);
        Assert.DoesNotContain("motdepasse1", admin.Html + (await u.MailsAsync("dir@exemple.tg"))[0].Html);
        Assert.True(await u.LiesAsync(db => db.AuditEintraege.AnyAsync(a => a.Aktion == PlattformAudit.SchuleRegistriert)));
    }

    [Theory]
    [InlineData("college  HORIZON")]
    [InlineData("Collège-Horizon")]
    [InlineData(" Collége Horizon ")]
    public async Task Schulname_ist_normalisiert_eindeutig(string name)
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction(), null);

        var f = await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.RegistrierenAsync(PlattformUmgebung.Direction(name, "autre@exemple.tg"), null));
        Assert.Equal(("ecole-nom", RegistrierungService.MeldungSchuleVorhanden), (f.Feld, f.Message));
        Assert.Equal(1, await u.LiesAsync(db => db.Schulen.CountAsync()));
    }

    [Fact]
    public async Task Vorpruefung_meldet_Felder_und_legt_nichts_an_ohne_Bedingungen_keine_Anlage()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        var gut = PlattformUmgebung.Direction();

        Assert.Equal("email", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Email = null }))).Feld);
        Assert.Equal("motdepasse", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Passwort = "abc", Bestaetigung = "abc" }))).Feld);
        Assert.Equal("confirmation", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Bestaetigung = "autre123" }))).Feld);
        Assert.Equal("ville", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Ort = " " }))).Feld);
        // Telefon ist für die Direktion Pflicht (16.11.2026: man muss sie leicht erreichen können), mindestens 8 Ziffern.
        var ohneTelefon = await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Telefon = " " }));
        Assert.Equal(("telephone", RegistrierungService.MeldungTelefonPflicht), (ohneTelefon.Feld, ohneTelefon.Message));
        var zuKurz = await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Telefon = "90 12 34" }));
        Assert.Equal(("telephone", RegistrierungService.MeldungTelefonKurz), (zuKurz.Feld, zuKurz.Message));
        Assert.Equal("telephone", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Telefon = "appelez-moi" }))).Feld);
        await u.Basis.KontoAsync("kossi.mensah", Rollen.Lehrer, (await u.Basis.SchuleAsync("Autre")).Id);
        Assert.Equal("identifiant", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.PruefeAsync(gut with { Identifiant = "Kossi.Mensah" }))).Feld);
        await u.Registrierung.PruefeAsync(gut); // ohne Identifiant: Vorschlag « kossi.mensah2 »

        var agb = await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.RegistrierenAsync(gut with { AgbAkzeptiert = false }, null));
        Assert.Equal(RegistrierungService.MeldungAgb, agb.Message);
        Assert.Equal(1, await u.LiesAsync(db => db.Schulen.CountAsync())); // nur « Autre »
        Assert.Equal("kossi.mensah2", (await u.Registrierung.RegistrierenAsync(gut, null)).Identifiant);
    }

    [Fact]
    public async Task Bremse_begrenzt_Registrierungen_je_Adresse_und_Verbindung()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        var schule = await u.Basis.SchuleAsync();
        for (var i = 0; i < RegistrierungsBremse.ProAdresse; i++)
            await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(schule.Id, "eleve@exemple.tg") with { Identifiant = $"eleve{i}" }, "10.0.0.9");

        var f = await Assert.ThrowsAsync<FachFehler>(() =>
            u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(schule.Id, "eleve@exemple.tg") with { Identifiant = "eleve9" }, "10.0.0.8"));
        Assert.Equal(RegistrierungsBremse.Meldung, f.Message);
        // Andere Adresse, gleiche Verbindung: noch erlaubt (Grenze je Verbindung höher).
        await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(schule.Id, "autre@exemple.tg") with { Identifiant = "eleve8" }, "10.0.0.9");

        var bremse = new RegistrierungsBremse(u.Zeit);
        for (var i = 0; i < RegistrierungsBremse.ProVerbindung; i++) bremse.Merken("1.2.3.4", null);
        Assert.False(bremse.IstErlaubt("1.2.3.4", null));
        Assert.True(bremse.IstErlaubt("1.2.3.5", null));
        u.Zeit.Vor(TimeSpan.FromHours(25));
        Assert.True(bremse.IstErlaubt("1.2.3.4", null));
    }

    [Fact]
    public async Task Eine_Klasse_registriert_sich_vom_selben_Anschluss()
    {
        // S4: Schul-WLAN/Informatikraum – alle Schüler kommen von EINER Verbindung. Früher war nach 10 für 24 Stunden Schluss.
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        var schule = await u.Basis.SchuleAsync();
        for (var i = 0; i < RegistrierungsBremse.ProVerbindung + 5; i++)
            await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(schule.Id, $"eleve{i}@exemple.tg") with { Identifiant = $"classe{i}" }, "10.0.0.50");

        // Schüler und Schulen zählen getrennt: die Grenze für Schulen bleibt eng, die für Schüler trägt eine ganze Schule.
        var bremse = new RegistrierungsBremse(u.Zeit);
        for (var i = 0; i < RegistrierungsBremse.ProVerbindung; i++) bremse.Merken("1.2.3.4", null, RegistrierungsArt.Direction);
        Assert.False(bremse.IstErlaubt("1.2.3.4", null, RegistrierungsArt.Direction));
        Assert.True(bremse.IstErlaubt("1.2.3.4", null, RegistrierungsArt.Eleve));
        for (var i = 0; i < RegistrierungsBremse.ProVerbindungEleves; i++) bremse.Merken("1.2.3.4", null, RegistrierungsArt.Eleve);
        Assert.False(bremse.IstErlaubt("1.2.3.4", null, RegistrierungsArt.Eleve));
        Assert.True(RegistrierungsBremse.ProVerbindungEleves >= 200);
    }

    [Fact]
    public async Task Schueler_nur_bei_aktiver_Schule_und_Uebergabe_an_die_Direktion()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        var wartend = await u.Basis.SchuleAsync("Wartend", SchulStatus.EnAttente);
        var aktiv = await u.Basis.SchuleAsync("Ecole Active");

        Assert.Equal("ecole", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(wartend.Id), null))).Feld);
        Assert.Equal("ecole", (await Assert.ThrowsAsync<FachFehler>(() => u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(null), null))).Feld);
        Assert.Empty(await u.Registrierung.SchulenSuchenAsync("Wartend"));
        Assert.Single(await u.Registrierung.SchulenSuchenAsync("école act"));

        var e = await u.Registrierung.RegistrierenAsync(PlattformUmgebung.Eleve(aktiv.Id, "ama@exemple.tg"), null);
        var konto = await u.LiesAsync(db => db.Users.SingleAsync(k => k.UserName == e.Identifiant));
        Assert.Equal((KontoStatus.EnAttente, Rollen.Schueler, aktiv.Id), (konto.Status, konto.Rolle, konto.SchuleId));
        Assert.Equal(AnmeldeErgebnis.KontoInPruefung, AnmeldeRegeln.Pruefe(konto.Status, konto.Rolle, SchulStatus.Active));
        Assert.Equal("+228 90 00 00 00", (await u.LiesAsync(db => db.SchuelerProfile.SingleAsync(p => p.KontoId == konto.Id))).ElternTelefon);
        Assert.Single(await u.MailsAsync("ama@exemple.tg"));

        // Übergabe: die Direktion validiert die Anmeldung auf /direction/eleves (Bereich E3).
        var direktor = await u.Basis.KontoAsync("dir.actif", Rollen.Direktor, aktiv.Id);
        Guid klasseId;
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            var jahr = new Schuljahr { SchuleId = aktiv.Id, Bezeichnung = "2026-2027", Beginn = new(2026, 9, 1), Ende = new(2027, 6, 30), Status = SchuljahrStatus.Laufend };
            var klasse = new Klasse { SchuleId = aktiv.Id, SchuljahrId = jahr.Id, Name = "6e A" };
            db.AddRange(jahr, klasse);
            await db.SaveChangesAsync();
            klasseId = klasse.Id;
        }
        var org = new OrgDienste(u.Db, u.Zeit);
        await org.Schueler.ValidierenAsync(direktor.Id, konto.Id, klasseId, false);
        var danach = await u.LiesAsync(db => db.Users.SingleAsync(k => k.Id == konto.Id));
        Assert.Equal(KontoStatus.Actif, danach.Status);
        Assert.NotNull((await u.LiesAsync(db => db.SchuelerProfile.SingleAsync(p => p.KontoId == konto.Id))).Matrikel);
    }
}
