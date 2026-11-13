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

    private static ClaimsPrincipal Nutzer(string rolle)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, rolle)], "Test"));
}
