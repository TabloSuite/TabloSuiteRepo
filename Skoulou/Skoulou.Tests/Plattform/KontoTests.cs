using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Nutzungsbedingungen, Pflicht-Passwortwechsel, Profil, Passwort vergessen/zurücksetzen, Kontakt.</summary>
public class KontoTests
{
    [Fact]
    public async Task Bedingungen_akzeptieren_schaltet_Zugriff_frei_und_protokolliert()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var lehrer = await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id, agbAkzeptiert: false);
        var konten = u.Hole<KontoService>();

        var stand = await konten.AgbStandAsync(lehrer.Id);
        Assert.True(stand.MussAkzeptieren);
        Assert.Equal(Rollen.Lehrer, stand.Rolle);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Hole<Zugriff>().PruefeAsync(lehrer.Id, Faehigkeit.NotenErfassen));

        await konten.AgbAkzeptierenAsync(lehrer.Id);
        await konten.AgbAkzeptierenAsync(lehrer.Id); // zweites Mal ändert nichts
        Assert.False((await konten.AgbStandAsync(lehrer.Id)).MussAkzeptieren);
        await u.Hole<Zugriff>().PruefeAsync(lehrer.Id, Faehigkeit.NotenErfassen);

        await using var db = await u.Db.CreateDbContextAsync();
        Assert.Equal(1, await db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.AgbAkzeptiert && a.AkteurId == lehrer.Id));

        // Neue Version → erneute Zustimmung nötig
        (await db.PlattformEinstellungen.SingleAsync()).AgbVersion = "2027-01-01";
        await db.SaveChangesAsync();
        Assert.True((await konten.AgbStandAsync(lehrer.Id)).MussAkzeptieren);
    }

    [Fact]
    public void Jede_Rolle_hat_genau_einen_Bedingungstext()
    {
        Assert.Equal("administration", AgbTexte.Fuer(Rollen.SuperAdmin).Id);
        Assert.Equal("direction", AgbTexte.Fuer(Rollen.Koordinator).Id);
        Assert.Equal("parent", AgbTexte.Fuer(Rollen.Eltern).Id);
        Assert.Equal("eleve", AgbTexte.Fuer(Rollen.Schueler).Id);
        var alleTexte = string.Join(' ', AgbTexte.Alle.SelectMany(a => a.Absaetze.Concat(a.Punkte)));
        Assert.DoesNotContain("abonnement", alleTexte, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commission", AgbTexte.Enseignant.Absaetze.First() + AgbTexte.Enseignant.Absaetze.Last(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pflicht_Passwortwechsel_wird_vorab_geprueft_und_beendet_andere_Sitzungen()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var konto = await u.KontoAsync("eleve.a", Rollen.Schueler, schule.Id);
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            (await db.Users.SingleAsync()).MussPasswortAendern = true;
            await db.SaveChangesAsync();
        }
        var konten = u.Hole<KontoService>();
        Assert.True((await konten.AgbStandAsync(konto.Id)).MussPasswortAendern);
        Assert.Equal(PasswortService.MeldungAktuellFalsch, await konten.PruefePasswortWechselAsync(konto.Id, "falsch1", "neues1x", "neues1x"));
        Assert.Equal(PasswortService.MeldungBestaetigung, await konten.PruefePasswortWechselAsync(konto.Id, TestUmgebung.Passwort, "neues1x", "neues2x"));
        Assert.Null(await konten.PruefePasswortWechselAsync(konto.Id, TestUmgebung.Passwort, "neues1x", "neues1x"));

        var sitzungVorher = Guid.NewGuid();
        var (fehler, geaendert) = await u.Hole<PasswortService>().AendernAsync(konto.Id, TestUmgebung.Passwort, "neues1x", "neues1x");
        Assert.Null(fehler);
        Assert.NotNull(geaendert);
        await using var db2 = await u.Db.CreateDbContextAsync();
        var nachher = await db2.Users.SingleAsync();
        Assert.False(nachher.MussPasswortAendern);
        Assert.NotEqual(sitzungVorher, nachher.AktiveSitzungId);
        Assert.Equal(AnmeldeErgebnis.Erlaubt, (await u.Hole<AnmeldeService>().PruefeAsync("eleve.a", "neues1x")).Ergebnis);
        Assert.Equal(1, await db2.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.PasswortGeaendert));
        Assert.Equal(0, await db2.MailAusgaenge.CountAsync()); // Konto ohne E-Mail
    }

    [Fact]
    public async Task Passwort_vergessen_und_zuruecksetzen_ohne_Passwort_in_der_Mail()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var konto = await u.KontoAsync("parent.a", Rollen.Eltern, schule.Id, email: "famille@exemple.tg");
        await u.KontoAsync("parent.b", Rollen.Eltern, schule.Id, email: "famille@exemple.tg");
        var passwoerter = u.Hole<PasswortService>();

        await passwoerter.AnfordernAsync("inconnu@exemple.tg");
        await passwoerter.AnfordernAsync("FAMILLE@exemple.tg");
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            var mail = await db.MailAusgaenge.SingleAsync();
            Assert.Equal("famille@exemple.tg", mail.An);
            Assert.Contains("parent.a", mail.Html);
            Assert.Contains("parent.b", mail.Html);
            Assert.Contains("https://skoulou.test/auth/reinitialiser?id=", mail.Html);
            Assert.DoesNotContain(TestUmgebung.Passwort, mail.Html);
        }

        var token = await u.Hole<Microsoft.AspNetCore.Identity.UserManager<Konto>>().GeneratePasswordResetTokenAsync(
            (await u.Hole<Microsoft.AspNetCore.Identity.UserManager<Konto>>().FindByIdAsync(konto.Id.ToString()))!);
        Assert.Equal(PasswortService.MeldungUngueltigerLink, (await passwoerter.ZuruecksetzenAsync(konto.Id, "faux", "nouveau9", "nouveau9")).Fehler);
        Assert.Equal(PasswortService.MeldungUnvollstaendig, (await passwoerter.ZuruecksetzenAsync(Guid.Empty, token, "nouveau9", "nouveau9")).Fehler);
        var (fehler, identifiant) = await passwoerter.ZuruecksetzenAsync(konto.Id, token, "nouveau9", "nouveau9");
        Assert.Null(fehler);
        Assert.Equal("parent.a", identifiant);
        Assert.Equal(AnmeldeErgebnis.Erlaubt, (await u.Hole<AnmeldeService>().PruefeAsync("parent.a", "nouveau9")).Ergebnis);
    }

    [Fact]
    public async Task Profil_wird_validiert_und_gespeichert()
    {
        await using var u = new TestUmgebung();
        await u.SeedAsync();
        var schule = await u.SchuleAsync("A");
        var konto = await u.KontoAsync("prof.a", Rollen.Lehrer, schule.Id);
        var konten = u.Hole<KontoService>();

        await Assert.ThrowsAsync<FachFehler>(() => konten.SpeichereProfilAsync(konto.Id, new ProfilEingabe(" ", "Nom", null, Geschlecht.Divers)));
        await Assert.ThrowsAsync<FachFehler>(() => konten.SpeichereProfilAsync(konto.Id, new ProfilEingabe("Ama", "Nom", "abc", Geschlecht.Divers)));
        await konten.SpeichereProfilAsync(konto.Id, new ProfilEingabe(" Ama ", "Mensah", "+228 90 00 00 00", Geschlecht.Weiblich));
        var profil = await konten.LadeProfilAsync(konto.Id);
        Assert.Equal("Ama", profil.Vorname);
        Assert.Equal("Enseignant", profil.Rolle);
        Assert.Equal("A", profil.Schule);
    }

    [Theory]
    [InlineData("+228 90 12 34 56", "https://wa.me/22890123456")]
    [InlineData("0022890123456", "https://wa.me/22890123456")]
    [InlineData("90 12 34 56", null)]
    [InlineData("+228 12", null)]
    [InlineData(null, null)]
    public void WhatsApp_Link_nur_fuer_internationale_Nummern(string? telefon, string? erwartet)
        => Assert.Equal(erwartet, KontaktService.WhatsAppLink(telefon));

    [Fact]
    public void Kontaktformular_Validierung()
    {
        Assert.NotNull(KontaktService.Pruefe(new KontaktEingabe("", "a@exemple.tg", "Sujet", "Un message assez long")));
        Assert.NotNull(KontaktService.Pruefe(new KontaktEingabe("Ama", "faux", "Sujet", "Un message assez long")));
        Assert.Equal("Le message doit contenir au moins 10 caractères.", KontaktService.Pruefe(new KontaktEingabe("Ama", "a@exemple.tg", "Sujet", "Court")));
        Assert.Null(KontaktService.Pruefe(new KontaktEingabe("Ama", "a@exemple.tg", "Sujet", "Un message assez long")));
    }
}
