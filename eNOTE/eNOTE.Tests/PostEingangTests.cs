using ENote.Core.DataModels.Identity;
using ENote.WebApp.Services.Mails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ENote.Tests;

/// <summary>Eine Adresse für alle Apps: « [eNOTE] » im Betreff, Posteingang nur für den Admin, keine Doppel.</summary>
public class PostEingangTests
{
    [Theory]
    [InlineData("Paiement confirmé", "[eNOTE] Paiement confirmé")]
    [InlineData("[eNOTE] Facture", "[eNOTE] Facture")]
    [InlineData("Re: [eNOTE] Facture", "Re: [eNOTE] Facture")]
    public void Betreff_traegt_immer_einmal_den_App_Namen(string betreff, string erwartet)
        => Assert.Equal(erwartet, EmailService.MitAppName(betreff));

    [Theory]
    [InlineData("Re: [eNOTE] Facture", true)]
    [InlineData("Question sur eNote", true)]
    [InlineData("Re: [SKOULOU] Bulletin", false)]
    [InlineData(null, false)]
    public void Zuordnung_an_eNOTE_ueber_den_Betreff(string? betreff, bool erwartet)
        => Assert.Equal(erwartet, PostEingangService.BetrifftEnote(betreff));

    [Fact]
    public async Task Historique_laedt_seitenweise_sucht_in_der_Datenbank_und_liefert_Inhalt_erst_beim_Oeffnen()
    {
        var factory = TestDbContextFactory.Create();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < 120; i++)
                db.MailNachrichten.Add(new ENote.Core.DataModels.Infrastruktur.MailNachricht
                {
                    Empfaenger = i == 3 ? "kofi@example.test" : $"kunde{i}@example.test",
                    Betreff = $"Facture {i}", HtmlInhalt = $"<p>Inhalt {i}</p>", CreatedAtUtc = start.AddMinutes(i)
                });
            await db.SaveChangesAsync();
        }
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);

        var (seite1, mehr1) = await email.SucheVerlaufAsync(null, 0);
        Assert.Equal(50, seite1.Count);
        Assert.True(mehr1);
        Assert.Equal("Facture 119", seite1[0].Betreff);           // neueste zuerst
        Assert.All(seite1, m => Assert.Equal("", m.HtmlInhalt));   // Inhalt nicht mitgeladen

        var (seite3, mehr3) = await email.SucheVerlaufAsync(null, 100);
        Assert.Equal(20, seite3.Count);
        Assert.False(mehr3);

        var (treffer, _) = await email.SucheVerlaufAsync("kofi@", 0);
        var kofi = Assert.Single(treffer);
        Assert.Equal("<p>Inhalt 3</p>", await email.GetInhaltAsync(kofi.Id));
    }

    [Fact]
    public async Task Versendete_Mails_tragen_den_App_Namen_im_Betreff()
    {
        var factory = TestDbContextFactory.Create();
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);

        await email.SendeAsync("ama@example.test", "Votre facture", "<p>x</p>", "message");

        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal("[eNOTE] Votre facture", (await db.MailNachrichten.SingleAsync()).Betreff);
    }

    [Fact]
    public async Task Doppelte_Zustellung_wird_nur_einmal_gespeichert_und_nur_der_Admin_liest()
    {
        var factory = TestDbContextFactory.Create();
        Guid admin, besitzer;
        await using (var db = await factory.CreateDbContextAsync())
        {
            admin = LadenRechteTests.LegeKontoAn(db, "admin", AppRollen.SuperAdmin);
            besitzer = LadenRechteTests.LegeKontoAn(db, "proprio", AppRollen.Besitzer);
            await db.SaveChangesAsync();
        }
        var email = new EmailService(new ConfigurationBuilder().Build(), factory, NullLogger<EmailService>.Instance);
        var dienst = new PostEingangService(factory, email, NullLogger<PostEingangService>.Instance);

        Assert.True(await dienst.EmpfangenAsync("Ama <ama@example.test>", "contact@tablosuite.com", "Re: [eNOTE] Facture", "Merci !", "pm-1"));
        Assert.False(await dienst.EmpfangenAsync("Ama <ama@example.test>", "contact@tablosuite.com", "Re: [eNOTE] Facture", "Merci !", "pm-1"));

        var liste = await dienst.LadeAsync(admin);
        Assert.Single(liste);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dienst.LadeAsync(besitzer));

        // Antwort: zu kurz → Hinweis; sonst wird sie in die Queue gestellt (Versand hier nicht konfiguriert → Fehlermeldung)
        Assert.NotNull(await dienst.AntwortenAsync(admin, liste[0].Id, " "));
        Assert.NotNull(await dienst.AntwortenAsync(admin, liste[0].Id, "Bonjour Ama, merci pour votre message."));
        await using var pruefung = await factory.CreateDbContextAsync();
        var raus = await pruefung.MailNachrichten.SingleAsync();
        Assert.Equal("ama@example.test", raus.Empfaenger);
        Assert.Equal("Re: [eNOTE] Facture", raus.Betreff);
    }
}
