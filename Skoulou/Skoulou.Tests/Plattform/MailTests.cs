using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.Tests.Plattform;

/// <summary>Outbox: Präfix, Layout, Textfassung, Wiederholung mit Abstand, endgültiger Fehlschlag.</summary>
public class MailTests
{
    [Theory]
    [InlineData("Bienvenue", "[SKOULOU] Bienvenue")]
    [InlineData("[SKOULOU] Bienvenue", "[SKOULOU] Bienvenue")]
    [InlineData("Re: [skoulou] Question", "Re: [skoulou] Question")]
    public void Praefix_einmalig(string betreff, string erwartet) => Assert.Equal(erwartet, MailAusgangService.MitAppKennung(betreff));

    [Fact]
    public async Task Einreihen_mit_Layout_Text_und_Praefix()
    {
        await using var u = new TestUmgebung();
        var id = await u.Hole<MailAusgangService>().EinreihenAsync("parent@exemple.tg", "Reçu de paiement",
            MailLayout.Absatz("Bonjour & bienvenue") + MailLayout.Knopf("https://skoulou.test/x", "Voir"), "test", antwortAn: "faux");
        Assert.NotNull(id);
        await using var db = await u.Db.CreateDbContextAsync();
        var mail = await db.MailAusgaenge.SingleAsync();
        Assert.Equal("[SKOULOU] Reçu de paiement", mail.Betreff);
        Assert.Contains(MailLayout.Marker, mail.Html);
        Assert.DoesNotContain("<style", mail.Html);
        Assert.Contains("Bonjour & bienvenue", mail.Text);
        Assert.Contains("Voir (https://skoulou.test/x)", mail.Text);
        Assert.Null(mail.AntwortAn);
        Assert.Equal(MailStatus.Offen, mail.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-une-adresse")]
    [InlineData("a@b")]
    public async Task Unbrauchbare_Adresse_wird_verworfen(string adresse)
    {
        await using var u = new TestUmgebung();
        Assert.Null(await u.Hole<MailAusgangService>().EinreihenAsync(adresse, "Test", "x"));
        await using var db = await u.Db.CreateDbContextAsync();
        Assert.Equal(0, await db.MailAusgaenge.CountAsync());
    }

    [Fact]
    public async Task Fehlversuche_werden_mit_Abstand_wiederholt_bis_zum_Fehlschlag()
    {
        await using var u = new TestUmgebung();
        var dienst = u.Hole<MailAusgangService>();
        await dienst.EinreihenAsync("eleve@exemple.tg", "Test", "Bonjour");
        u.Versender.Ergebnis = _ => new VersandErgebnis(false, null, "Postmark 500");

        Assert.Equal(0, await dienst.VerarbeiteFaelligeAsync());
        var mail = await LadeAsync(u);
        Assert.Equal(1, mail.Versuche);
        Assert.Equal(MailStatus.Offen, mail.Status);
        Assert.Equal(u.Zeit.Jetzt.UtcDateTime + TimeSpan.FromMinutes(1), mail.NaechsterVersuchUtc);

        // Vor Ablauf der Wartezeit passiert nichts.
        await dienst.VerarbeiteFaelligeAsync();
        Assert.Equal(1, (await LadeAsync(u)).Versuche);

        for (var i = 2; i <= MailAusgangService.MaxVersuche; i++)
        {
            u.Zeit.Vor(TimeSpan.FromHours(5));
            await dienst.VerarbeiteFaelligeAsync();
        }
        mail = await LadeAsync(u);
        Assert.Equal(MailAusgangService.MaxVersuche, mail.Versuche);
        Assert.Equal(MailStatus.Fehlgeschlagen, mail.Status);
        Assert.Equal("Postmark 500", mail.Fehler);
    }

    [Fact]
    public async Task Erfolg_nach_Fehlversuch_und_ohne_Konfiguration_bleibt_alles_offen()
    {
        await using var u = new TestUmgebung();
        var dienst = u.Hole<MailAusgangService>();
        await dienst.EinreihenAsync("eleve@exemple.tg", "Test", "Bonjour");

        u.Versender.IstBereit = false;
        Assert.Equal(0, await dienst.VerarbeiteFaelligeAsync());
        Assert.Equal(0, (await LadeAsync(u)).Versuche);

        u.Versender.IstBereit = true;
        var versuche = 0;
        u.Versender.Ergebnis = _ => ++versuche == 1 ? new VersandErgebnis(false, null, "Timeout") : new VersandErgebnis(true, "pm-42", null);
        await dienst.VerarbeiteFaelligeAsync();
        u.Zeit.Vor(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await dienst.VerarbeiteFaelligeAsync());
        var mail = await LadeAsync(u);
        Assert.Equal(MailStatus.Gesendet, mail.Status);
        Assert.Equal("pm-42", mail.PostmarkId);
        Assert.Null(mail.Fehler);
        Assert.Equal(2, u.Versender.Gesendet.Count);
    }

    [Fact]
    public void Wartezeiten_wachsen()
    {
        Assert.True(MailAusgangService.Wartezeit(1) < MailAusgangService.Wartezeit(2));
        Assert.True(MailAusgangService.Wartezeit(4) < MailAusgangService.Wartezeit(5));
    }

    private static async Task<MailAusgang> LadeAsync(TestUmgebung u)
    {
        await using var db = await u.Db.CreateDbContextAsync();
        return await db.MailAusgaenge.AsNoTracking().SingleAsync();
    }
}
