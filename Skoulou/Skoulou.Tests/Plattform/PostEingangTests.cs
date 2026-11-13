using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Webhook /api/postmark/inbound: Basic-Auth, eigene Absender, eNOTE-Weiterleitung, Speichern.</summary>
public class PostEingangTests
{
    private static (PostEingangService Dienst, FakeHttp Http) Baue(TestUmgebung u, HttpStatusCode enoteAntwort = HttpStatusCode.OK, bool mitWeiterleitung = true)
    {
        var optionen = new TestOptionen<PostmarkOptionen>(new PostmarkOptionen
        {
            FromEmail = "noreply@tablosuite.com",
            InboundWebhookSecret = "geheim",
            WeiterleitungEnoteUrl = mitWeiterleitung ? "https://enote.test/api/postmark/inbound" : null,
            WeiterleitungEnoteGeheimnis = mitWeiterleitung ? "enote-geheim" : null
        });
        var http = new FakeHttp(enoteAntwort);
        var weiterleitung = new MailWeiterleitung(http, optionen, NullLogger<MailWeiterleitung>.Instance);
        return (new PostEingangService(u.Db, weiterleitung, new HtmlBereiniger(), optionen), http);
    }

    private static string Json(string von, string betreff, string id = "pm-1", string an = "SKOULOU <contact@tablosuite.com>", string html = "<p>Bonjour</p>")
        => JsonSerializer.Serialize(new { From = von, To = an, Subject = betreff, MessageID = id, HtmlBody = html, TextBody = "Bonjour" });

    private static async Task<int> AnzahlAsync(TestUmgebung u)
    {
        await using var db = await u.Db.CreateDbContextAsync();
        return await db.Nachrichten.CountAsync();
    }

    [Fact]
    public void Basic_Auth_mit_Benutzer_skoulou()
    {
        var richtig = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("skoulou:geheim"));
        Assert.True(PostEingangService.PruefeBasicAuth(richtig, "geheim"));
        Assert.False(PostEingangService.PruefeBasicAuth(richtig, "anders"));
        Assert.False(PostEingangService.PruefeBasicAuth("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("enote:geheim")), "geheim"));
        Assert.False(PostEingangService.PruefeBasicAuth(richtig, ""));
        Assert.False(PostEingangService.PruefeBasicAuth(null, "geheim"));
    }

    [Fact]
    public async Task Fremder_Empfaenger_wird_still_ignoriert()
    {
        await using var u = new TestUmgebung();
        var (dienst, _) = Baue(u);
        Assert.Equal(EingangsErgebnis.FremdesZiel, await dienst.VerarbeiteAsync(Json("a@exemple.tg", "Hallo", an: "autre@exemple.tg")));
        Assert.Equal(0, await AnzahlAsync(u));
    }

    [Fact]
    public async Task Eigene_Absender_werden_ignoriert()
    {
        await using var u = new TestUmgebung();
        var (dienst, _) = Baue(u);
        Assert.Equal(EingangsErgebnis.EigenerAbsender, await dienst.VerarbeiteAsync(Json("SKOULOU <noreply@tablosuite.com>", "[Contact] Test")));
        Assert.Equal(EingangsErgebnis.EigenerAbsender, await dienst.VerarbeiteAsync(Json("CONTACT@tablosuite.com", "Test", "pm-2")));
        Assert.Equal(0, await AnzahlAsync(u));
    }

    [Fact]
    public async Task ENOTE_im_Betreff_wird_unveraendert_weitergeleitet()
    {
        await using var u = new TestUmgebung();
        var (dienst, http) = Baue(u);
        var json = Json("salon@exemple.tg", "Question sur eNOTE");
        Assert.Equal(EingangsErgebnis.Weitergeleitet, await dienst.VerarbeiteAsync(json));
        var (anfrage, inhalt) = Assert.Single(http.Anfragen);
        Assert.Equal(json, inhalt);
        Assert.Equal("Basic", anfrage.Headers.Authorization?.Scheme);
        Assert.Equal("enote:enote-geheim", Encoding.UTF8.GetString(Convert.FromBase64String(anfrage.Headers.Authorization!.Parameter!)));
        Assert.Equal(0, await AnzahlAsync(u));
    }

    [Fact]
    public async Task Fehlgeschlagene_Weiterleitung_landet_im_Posteingang()
    {
        await using var u = new TestUmgebung();
        var (dienst, _) = Baue(u, HttpStatusCode.InternalServerError);
        Assert.Equal(EingangsErgebnis.Gespeichert, await dienst.VerarbeiteAsync(Json("salon@exemple.tg", "eNOTE caisse")));
        var (ohne, http) = Baue(u, mitWeiterleitung: false);
        Assert.Equal(EingangsErgebnis.Gespeichert, await ohne.VerarbeiteAsync(Json("salon@exemple.tg", "eNOTE", "pm-2")));
        Assert.Empty(http.Anfragen);
        Assert.Equal(2, await AnzahlAsync(u));
    }

    [Fact]
    public async Task SKOULOU_Kennung_hat_Vorrang_vor_eNOTE()
    {
        await using var u = new TestUmgebung();
        var (dienst, http) = Baue(u);
        Assert.Equal(EingangsErgebnis.Gespeichert, await dienst.VerarbeiteAsync(Json("dir@exemple.tg", "Re: [SKOULOU] eNOTE ou SKOULOU ?")));
        Assert.Empty(http.Anfragen);
    }

    [Fact]
    public async Task Doppelte_Zustellung_ungueltige_Daten_und_Bereinigung()
    {
        await using var u = new TestUmgebung();
        var (dienst, _) = Baue(u);
        var html = "<p>Bonjour</p><script>alert(1)</script><img src=\"https://tracker.example/x.png\"><img src=\"/medien/abc\">";
        Assert.Equal(EingangsErgebnis.Gespeichert, await dienst.VerarbeiteAsync(Json("a@exemple.tg", "", "pm-9", html: html)));
        Assert.Equal(EingangsErgebnis.Doppelt, await dienst.VerarbeiteAsync(Json("a@exemple.tg", "", "pm-9")));
        Assert.Equal(EingangsErgebnis.Ungueltig, await dienst.VerarbeiteAsync("{ kein json"));
        Assert.Equal(EingangsErgebnis.Ungueltig, await dienst.VerarbeiteAsync(JsonSerializer.Serialize(new { From = "a@exemple.tg", To = "contact@tablosuite.com" })));

        await using var db = await u.Db.CreateDbContextAsync();
        var n = await db.Nachrichten.SingleAsync();
        Assert.Equal("(sans objet)", n.Betreff);
        Assert.Equal(NachrichtRichtung.Eingang, n.Richtung);
        Assert.DoesNotContain("<script", n.TextHtml);
        Assert.DoesNotContain("tracker.example", n.TextHtml);
        Assert.Contains("/medien/abc", n.TextHtml);
    }

    [Fact]
    public async Task Bekannter_Absender_wird_seiner_Schule_zugeordnet()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        await u.KontoAsync("dir.a", Rollen.Direktor, schule.Id, email: "dir@exemple.tg");
        var (dienst, _) = Baue(u);
        await dienst.VerarbeiteAsync(Json("Direction <DIR@exemple.tg>", "Question", "pm-1"));
        await dienst.VerarbeiteAsync(Json("inconnu@exemple.tg", "Question", "pm-2"));

        await using var db = await u.Db.CreateDbContextAsync();
        Assert.Equal(schule.Id, (await db.Nachrichten.SingleAsync(n => n.PostmarkId == "pm-1")).SchuleId);
        Assert.Null((await db.Nachrichten.SingleAsync(n => n.PostmarkId == "pm-2")).SchuleId);
    }
}
