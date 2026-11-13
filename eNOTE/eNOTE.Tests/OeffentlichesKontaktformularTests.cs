using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>Öffentliches Kontaktformular (ohne Anmeldung): Prüfung, Spam-Schutz, Ablage im Posteingang.</summary>
public class OeffentlichesKontaktformularTests
{
    private const string Thema = "Question sur eNOTE";
    private const string Text = "Bonjour, combien coûte eNOTE pour deux boutiques ?";

    [Fact]
    public async Task Nachricht_landet_mit_Absender_im_Posteingang()
    {
        KontaktService.SetzeZaehlerZurueck();
        var factory = TestDbContextFactory.Create();
        var dienst = new KontaktService(factory);

        Assert.Null(await dienst.SendeOeffentlichAsync("Ama Mensah", "ama@example.test", "+228 90 00 00 00", Thema, Text, null));

        await using var db = await factory.CreateDbContextAsync();
        var mail = await db.EingangsMails.SingleAsync();
        Assert.Equal("[eNOTE] Contact : " + Thema, mail.Betreff);
        Assert.True(System.Net.Mail.MailAddress.TryCreate(mail.Absender, out var adresse));
        Assert.Equal("ama@example.test", adresse!.Address);
        Assert.Contains("+228 90 00 00 00", mail.Text);
    }

    [Theory]
    [InlineData("", "ama@example.test", Thema, Text, "nom")]
    [InlineData("Ama", "keine-adresse", Thema, Text, "e-mail")]
    [InlineData("Ama", "ama@example.test", "Erfundenes Thema", Text, "sujet")]
    [InlineData("Ama", "ama@example.test", Thema, "court", "au moins")]
    public async Task Eingaben_werden_geprueft(string name, string email, string thema, string text, string erwartet)
    {
        KontaktService.SetzeZaehlerZurueck();
        var fehler = await new KontaktService(TestDbContextFactory.Create()).SendeOeffentlichAsync(name, email, null, thema, text, null);
        Assert.NotNull(fehler);
        Assert.Contains(erwartet, fehler);
    }

    [Fact]
    public async Task Bots_mit_ausgefuelltem_Fangfeld_werden_still_verworfen()
    {
        KontaktService.SetzeZaehlerZurueck();
        var factory = TestDbContextFactory.Create();

        Assert.Null(await new KontaktService(factory).SendeOeffentlichAsync("Bot", "bot@example.test", null, Thema, Text, "https://spam.example"));

        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(db.EingangsMails);
    }

    [Fact]
    public async Task Hoechstens_drei_Nachrichten_pro_Adresse_und_Stunde()
    {
        KontaktService.SetzeZaehlerZurueck();
        var dienst = new KontaktService(TestDbContextFactory.Create());
        var jetzt = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < KontaktService.OeffentlichMaxProAdresseProStunde; i++)
            Assert.Null(await dienst.SendeOeffentlichAsync("Ama", "ama@example.test", null, Thema, Text, null, jetztUtc: jetzt));
        Assert.NotNull(await dienst.SendeOeffentlichAsync("Ama", "AMA@example.test", null, Thema, Text, null, jetztUtc: jetzt));

        // Eine Stunde später wieder möglich
        Assert.Null(await dienst.SendeOeffentlichAsync("Ama", "ama@example.test", null, Thema, Text, null, jetztUtc: jetzt.AddMinutes(61)));
    }
}
