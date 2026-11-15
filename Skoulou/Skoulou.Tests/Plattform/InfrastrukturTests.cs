using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Components.Layout;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Nummernkreis (InMemory), Navigation ↔ Seiten, Videokatalog, Medienpfade, Aufräumen.</summary>
public class InfrastrukturTests
{
    [Fact]
    public async Task Nummernkreis_zaehlt_je_Schule_und_global()
    {
        await using var u = new TestUmgebung();
        var dienst = u.Hole<NummernkreisService>();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using var db = await u.Db.CreateDbContextAsync();

        Assert.Equal("REC-2026-00001", await dienst.NaechsteNummerAsync(db, a, NummernArt.Quittung, 2026));
        Assert.Equal("REC-2026-00002", await dienst.NaechsteNummerAsync(db, a, NummernArt.Quittung, 2026));
        Assert.Equal("REC-2026-00001", await dienst.NaechsteNummerAsync(db, b, NummernArt.Quittung, 2026));
        Assert.Equal("REC-2027-00001", await dienst.NaechsteNummerAsync(db, a, NummernArt.Quittung, 2027));
        Assert.Equal("SKL-2026-0001", await dienst.NaechsteNummerAsync(db, a, NummernArt.Plattformrechnung, 2026));
        Assert.Equal("SKL-2026-0002", await dienst.NaechsteNummerAsync(db, b, NummernArt.Plattformrechnung, 2026));
        Assert.Null(await db.Nummernkreise.Where(k => k.Art == NummernArt.Plattformrechnung).Select(k => k.SchuleId).SingleAsync());
    }

    [Fact]
    public void Jede_Navigationsroute_hat_eine_Seite()
    {
        var routen = typeof(Program).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template.Split('{')[0].TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        static bool Alle(KoordinatorRechte _) => true;
        foreach (var rolle in Rollen.Alle)
        {
            foreach (var e in NavKarte.Fuer(rolle, Alle).SelectMany(g => g.Eintraege))
                Assert.True(routen.Contains(e.Href), $"Keine Seite für {e.Href} ({rolle})");
            Assert.Equal(4, NavKarte.Mobil(rolle, Alle).Count);
            Assert.Contains(Rollen.Startseite(rolle), routen);
        }
        foreach (var oeffentlich in new[] { "", "/auth/connexion", "/auth/inscription", "/auth/mot-de-passe-oublie", "/auth/reinitialiser",
                     "/contact", "/conditions", "/verification", "/profil", "/aide/videos", "/aide/guide", "/acces-refuse" })
            Assert.Contains(oeffentlich, routen);
    }

    [Fact]
    public void Koordinator_sieht_Finanzen_nur_mit_Recht_und_nie_die_Schulverwaltung()
    {
        var ohne = NavKarte.Fuer(Rollen.Koordinator, _ => false).SelectMany(g => g.Eintraege).Select(e => e.Href).ToList();
        Assert.DoesNotContain("/finances", ohne);
        Assert.DoesNotContain("/direction/ecole", ohne);
        var mitZahlungen = NavKarte.Fuer(Rollen.Koordinator, r => r == KoordinatorRechte.ZahlungenErfassen)
            .SelectMany(g => g.Eintraege).Select(e => e.Href).ToList();
        Assert.Contains("/finances/paiements", mitZahlungen);
        Assert.DoesNotContain("/finances/journal", mitZahlungen);
        Assert.Contains("/direction/ecole", NavKarte.Fuer(Rollen.Direktor, _ => true).SelectMany(g => g.Eintraege).Select(e => e.Href));
    }

    [Fact]
    public void Videos_nach_Rolle_Werbung_oeffentlich()
    {
        var anonym = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.True(VideoKatalog.DarfAbrufen(VideoKatalog.Werbung, anonym));
        Assert.False(VideoKatalog.DarfAbrufen("skoulou-pourquoi.mp4", anonym));

        var eltern = Nutzer(Rollen.Eltern);
        Assert.True(VideoKatalog.DarfAbrufen("skoulou-parent-suivre.mp4", eltern));
        Assert.False(VideoKatalog.DarfAbrufen("skoulou-direction-notes.mp4", eltern));
        Assert.Equal(VideoKatalog.PourTous, VideoKatalog.FuerNutzer(eltern)[0].Fuer);
        Assert.Equal(VideoKatalog.Parent, VideoKatalog.FuerNutzer(eltern)[1].Fuer);

        var direktor = Nutzer(Rollen.Direktor);
        Assert.Equal(5, VideoKatalog.FuerNutzer(direktor).Count);
        Assert.False(VideoKatalog.DarfAbrufen("inconnu.mp4", Nutzer(Rollen.SuperAdmin)));
        Assert.DoesNotMatch(VideoKatalog.DateiMuster(), "../secret.mp4");
    }

    [Fact]
    public void Medienpfad_Schema_und_Schutz()
    {
        var schule = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var medium = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        Assert.Equal("schulen/11111111222233334444555555555555/logo/aaaaaaaabbbbccccddddeeeeeeeeeeee", MedienPfad.Fuer(schule, MedienZweck.Logo, medium));
        Assert.StartsWith("plattform/video/", MedienPfad.Fuer(null, MedienZweck.Video, medium));
        Assert.False(MedienPfad.IstGueltig("../etc/passwd"));
        Assert.False(MedienPfad.IstGueltig("C:/x"));
        Assert.True(BlobMedienSpeicher.IstGueltigeServiceUri("https://konto.blob.core.windows.net/"));
        Assert.False(BlobMedienSpeicher.IstGueltigeServiceUri("https://konto.blob.core.windows.net/container?sv=1"));
        Assert.False(BlobMedienSpeicher.IstGueltigeServiceUri("http://konto.blob.core.windows.net/"));
    }

    [Fact]
    public async Task Lokaler_Speicher_nie_unter_wwwroot_und_ohne_Pfadausbruch()
    {
        var basis = Path.Combine(Path.GetTempPath(), "skoulou-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Throws<InvalidOperationException>(() => new LokalerMedienSpeicher(Path.Combine(basis, "wwwroot", "medien"), Path.Combine(basis, "wwwroot")));
            var speicher = new LokalerMedienSpeicher(Path.Combine(basis, "daten"), Path.Combine(basis, "wwwroot"));
            var name = MedienPfad.Fuer(Guid.NewGuid(), MedienZweck.Beleg, Guid.NewGuid());
            Assert.Equal(5, await speicher.SpeichernAsync(name, new MemoryStream([1, 2, 3, 4, 5]), "application/pdf"));
            await using (var strom = await speicher.OeffnenAsync(name)) Assert.NotNull(strom);
            Assert.True(await speicher.LoeschenAsync(name));
            Assert.Null(await speicher.OeffnenAsync(name));
            await Assert.ThrowsAsync<ArgumentException>(() => speicher.SpeichernAsync("../ausbruch", new MemoryStream(), "text/plain"));
        }
        finally
        {
            if (Directory.Exists(basis)) Directory.Delete(basis, true);
        }
    }

    [Fact]
    public async Task Aufraeumen_entfernt_alte_Fehler_und_Mails()
    {
        await using var u = new TestUmgebung();
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            var jetzt = u.Zeit.Jetzt.UtcDateTime;
            db.FehlerProtokolle.AddRange(new FehlerProtokoll { ZeitUtc = jetzt.AddDays(-91), Nachricht = "alt" }, new FehlerProtokoll { ZeitUtc = jetzt.AddDays(-5), Nachricht = "neu" });
            db.MailAusgaenge.AddRange(
                new MailAusgang { An = "a@exemple.tg", Status = MailStatus.Gesendet, ErstelltAmUtc = jetzt.AddDays(-800) },
                new MailAusgang { An = "b@exemple.tg", Status = MailStatus.Gesendet, ErstelltAmUtc = jetzt.AddDays(-10) });
            await db.SaveChangesAsync();
        }
        var dienst = new AufraeumDienst(u.Db, u.Zeit, Microsoft.Extensions.Logging.Abstractions.NullLogger<AufraeumDienst>.Instance);
        var (fehler, mails, _) = await dienst.BereinigeAsync();
        Assert.Equal(1, fehler);
        Assert.Equal(1, mails);
        await using var db2 = await u.Db.CreateDbContextAsync();
        Assert.Equal("neu", (await db2.FehlerProtokolle.SingleAsync()).Nachricht);
    }

    [Fact]
    public void Html_wird_bereinigt()
    {
        var b = new HtmlBereiniger();
        var html = b.Bereinige("<p onclick=\"x()\">Hi</p><a href=\"javascript:alert(1)\">x</a><img src=\"https://evil/x.png\"><img src=\"/medien/1\">");
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("evil", html);
        Assert.Contains("/medien/1", html);
        Assert.Equal("a &lt;b&gt;<br>c", HtmlBereiniger.TextAlsHtml("a <b>\nc"));
    }

    // ---------- Rate-Limits (S4): eine Klasse am selben Anschluss darf sich nicht gegenseitig aussperren ----------

    [Fact]
    public void RateLimits_tragen_eine_ganze_Klasse_am_selben_Anschluss()
    {
        // Schul-WLAN/Informatikraum/Mobilfunk-NAT: alle Schüler kommen von EINER IP.
        Assert.True(RateLimits.AnmeldungenJeMinute >= 50, "eine Klasse meldet sich innerhalb einer Minute an");
        Assert.True(RateLimits.RegistrierungenJeZehnMinuten >= 100, "eine Klasse registriert sich selbst, auch mit Tippfehlern und zweitem Versuch");
        Assert.True(RateLimits.PasswortOublieJeZehnMinuten >= 20);
    }

    [Fact]
    public void Passwort_Limit_zaehlt_angemeldet_je_Konto_sonst_je_Anschluss()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        static ClaimsPrincipal Konto(Guid id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Test"));
        // Pflichtdialog beim ersten Login: zwei Schüler am selben Anschluss haben getrennte Zähler.
        Assert.NotEqual(RateLimits.PasswortSchluessel(Konto(a), "10.0.0.7"), RateLimits.PasswortSchluessel(Konto(b), "10.0.0.7"));
        Assert.Equal(RateLimits.PasswortSchluessel(Konto(a), "10.0.0.7"), RateLimits.PasswortSchluessel(Konto(a), "10.0.0.99"));
        Assert.Equal("ip:10.0.0.7", RateLimits.PasswortSchluessel(new ClaimsPrincipal(new ClaimsIdentity()), "10.0.0.7"));
        Assert.Equal("ip:unbekannt", RateLimits.PasswortSchluessel(null, null));
    }

    [Theory]
    // Rücksetz-Link: id und token bleiben erhalten (sonst « lien invalide » statt « trop de tentatives »)
    [InlineData("https://skoulou.test/auth/reinitialiser?id=7b1c&token=Cf%2BDJ8%3D", "/auth/reinitialiser?id=7b1c&token=Cf%2BDJ8%3D&erreur=limite")]
    // alte Meldungen fallen weg
    [InlineData("https://skoulou.test/auth/mot-de-passe-oublie?envoye=1", "/auth/mot-de-passe-oublie?erreur=limite")]
    [InlineData("https://skoulou.test/auth/connexion?erreur=identifiants&identifiant=ama.k", "/auth/connexion?identifiant=ama.k&erreur=limite")]
    [InlineData("https://skoulou.test/profil?erreur-mdp=1&info=x", "/profil?erreur=limite")]
    [InlineData("https://skoulou.test/direction", "/direction?erreur=limite")]
    public void Nach_einer_Ablehnung_geht_es_auf_dieselbe_Seite_mit_ihren_Parametern_zurueck(string seite, string erwartet)
        => Assert.Equal(erwartet, RateLimits.ZielNachAblehnung(new Uri(seite)));

    [Theory]
    // Azure, Weiterleitung noch nicht ausgewertet: der letzte Eintrag (vom Eingangsserver angehängt) ist der Besucher
    [InlineData("169.254.130.1", "203.0.113.5:51234", false, true, "203.0.113.5")]
    [InlineData("169.254.130.1", "10.9.9.9, 203.0.113.5:51234", false, true, "203.0.113.5")]   // erfundener Eintrag davor zählt nicht
    [InlineData("169.254.130.1", "[2001:db8::7]:443", false, true, "2001:db8::7")]
    // Azure, vom Framework schon ausgewertet (X-Original-For): die Verbindung IST der Besucher, Reste sind nicht vertrauenswürdig
    [InlineData("203.0.113.5", "10.9.9.9", true, true, "203.0.113.5")]
    // nicht hinter Azure: eine mitgeschickte Kopfzeile wird ignoriert
    [InlineData("198.51.100.20", "203.0.113.5", false, false, "198.51.100.20")]
    [InlineData("169.254.130.1", "", false, true, "169.254.130.1")]
    [InlineData("169.254.130.1", "pas-une-adresse", false, true, "169.254.130.1")]
    [InlineData(null, null, false, false, "unbekannt")]
    public void Anschluss_ist_die_Adresse_des_Besuchers_auch_hinter_dem_Azure_Eingangsserver(
        string? verbindung, string? weitergeleitet, bool schonAusgewertet, bool hinterAzure, string erwartet)
        => Assert.Equal(erwartet, Anschluss.Adresse(verbindung, weitergeleitet, schonAusgewertet, hinterAzure));

    [Fact]
    public void Meldung_zum_Limit_nennt_keine_feste_Minute()
    {
        // Fenster: Anmeldung 1 Minute, Passwort/Registrierung 10 Minuten – ein Text für alle Formulare.
        Assert.Equal(AnmeldeRegeln.MeldungLimite, AnmeldeRegeln.Meldung("limite"));
        Assert.Contains("quelques minutes", AnmeldeRegeln.MeldungLimite);
    }

    private static ClaimsPrincipal Nutzer(string rolle)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "Test"));
}
