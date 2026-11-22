using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Plattform;

/// <summary>Annonces (Zielgruppen, Zeitraum, Cache), Messagerie (Sichtbarkeit, Antworten, Zuordnung), Aufbewahrung, Medien.</summary>
public class KommunikationTests
{
    private static AnnonceEingabe Annonce(string titel, ZielRollen ziel, DateTime start, DateTime ende, Guid? schule = null)
        => new(titel, "Texte de l'annonce", AnkuendigungsStufe.Info, ziel, schule, start, ende);

    [Fact]
    public async Task Annonces_nach_Zielgruppe_Schule_und_Zeitraum_Cache_wird_beim_Speichern_geleert()
    {
        await using var u = new PlattformUmgebung();
        var admin = await u.StartAsync();
        var schule = await u.Basis.SchuleAsync();
        var jetzt = u.Zeit.Jetzt.UtcDateTime;

        await u.Annonces.SpeichernAsync(admin.Id, null, Annonce("Public", ZielRollen.Visiteurs, jetzt.AddHours(-1), jetzt.AddDays(1)));
        await u.Annonces.SpeichernAsync(admin.Id, null, Annonce("Direction", ZielRollen.Direktor | ZielRollen.Koordinator, jetzt.AddHours(-1), jetzt.AddDays(1)));
        await u.Annonces.SpeichernAsync(admin.Id, null, Annonce("Élèves école", ZielRollen.Schueler, jetzt.AddHours(-1), jetzt.AddDays(1), schule.Id));
        await u.Annonces.SpeichernAsync(admin.Id, null, Annonce("Plus tard", ZielRollen.Alle, jetzt.AddDays(2), jetzt.AddDays(3)));

        Assert.Equal(["Public"], (await u.Annonces.AktiveAsync(null, null)).Select(a => a.Titel));
        Assert.Equal(["Direction"], (await u.Annonces.AktiveAsync(Rollen.Koordinator, schule.Id)).Select(a => a.Titel));
        Assert.Equal(["Élèves école"], (await u.Annonces.AktiveAsync(Rollen.Schueler, schule.Id)).Select(a => a.Titel));
        Assert.Empty(await u.Annonces.AktiveAsync(Rollen.Schueler, Guid.NewGuid()));
        Assert.Empty(await u.Annonces.AktiveAsync(Rollen.Lehrer, schule.Id));

        // Neue Annonce ist sofort sichtbar (Cache geleert), abgelaufene verschwindet ohne Neustart.
        var id = await u.Annonces.SpeichernAsync(admin.Id, null, Annonce("Enseignants", ZielRollen.Lehrer, jetzt.AddMinutes(-1), jetzt.AddHours(2)));
        Assert.Single(await u.Annonces.AktiveAsync(Rollen.Lehrer, schule.Id));
        u.Zeit.Vor(TimeSpan.FromHours(3));
        Assert.Empty(await u.Annonces.AktiveAsync(Rollen.Lehrer, schule.Id));
        await u.Annonces.LoeschenAsync(admin.Id, id);
        Assert.Equal(4, (await u.Annonces.SucheAsync(admin.Id, null, null)).Eintraege.Count);
    }

    [Fact]
    public async Task Annonces_werden_geprueft()
    {
        await using var u = new PlattformUmgebung();
        var admin = await u.StartAsync();
        var jetzt = u.Zeit.Jetzt.UtcDateTime;
        var gut = Annonce("Titre", ZielRollen.Alle, jetzt, jetzt.AddDays(1));

        Assert.Equal("titre", (await Assert.ThrowsAsync<FachFehler>(() => u.Annonces.SpeichernAsync(admin.Id, null, gut with { Titel = " " }))).Feld);
        Assert.Equal("texte", (await Assert.ThrowsAsync<FachFehler>(() => u.Annonces.SpeichernAsync(admin.Id, null, gut with { Text = "abc" }))).Feld);
        Assert.Equal("public", (await Assert.ThrowsAsync<FachFehler>(() => u.Annonces.SpeichernAsync(admin.Id, null, gut with { Zielrollen = ZielRollen.Keine }))).Feld);
        Assert.Equal("fin", (await Assert.ThrowsAsync<FachFehler>(() => u.Annonces.SpeichernAsync(admin.Id, null, gut with { EndeUtc = jetzt.AddDays(-1) }))).Feld);
        var schuleId = (await u.Basis.SchuleAsync()).Id;
        Assert.Equal("public", (await Assert.ThrowsAsync<FachFehler>(() => u.Annonces.SpeichernAsync(admin.Id, null, gut with { SchuleId = schuleId }))).Feld);
        var id = await u.Annonces.SpeichernAsync(admin.Id, null, gut with { Text = "<b>Ligne 1</b>\nLigne 2" });
        var html = (await u.Annonces.AktiveAsync(Rollen.Eltern, null)).Single().InhaltHtml;
        Assert.Equal("&lt;b&gt;Ligne 1&lt;/b&gt;<br>Ligne 2", html);
        Assert.Equal("<b>Ligne 1</b>\nLigne 2", (await u.Annonces.LadenAsync(admin.Id, id)).Text);
    }

    [Fact]
    public async Task Messagerie_Sichtbarkeit_Antworten_und_Zuordnung()
    {
        await using var u = new PlattformUmgebung();
        var admin = await u.StartAsync();
        var resp = await u.Basis.KontoAsync("resp", Rollen.Responsable, email: "resp@exemple.tg");
        var eigene = await u.Basis.SchuleAsync("Eigene", responsable: resp.Id);
        var fremde = await u.Basis.SchuleAsync("Fremde");
        await u.Basis.KontoAsync("dir", Rollen.Direktor, eigene.Id, email: "dir@exemple.tg");
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            db.Nachrichten.AddRange(
                new Nachricht { Richtung = NachrichtRichtung.Eingang, Von = "dir@exemple.tg", An = SkoulouMarke.KontaktEmail, Betreff = "Eigene", TextHtml = "x", SchuleId = eigene.Id },
                new Nachricht { Richtung = NachrichtRichtung.Eingang, Von = "x@exemple.tg", An = SkoulouMarke.KontaktEmail, Betreff = "Fremde", TextHtml = "x", SchuleId = fremde.Id },
                new Nachricht { Richtung = NachrichtRichtung.Eingang, Von = "y@exemple.tg", An = SkoulouMarke.KontaktEmail, Betreff = "Ohne", TextHtml = "x" });
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, (await u.Messagerie.SucheAsync(admin.Id, Postfach.Eingang, null, null)).Eintraege.Count);
        var sicht = (await u.Messagerie.SucheAsync(resp.Id, Postfach.Eingang, null, null)).Eintraege;
        Assert.Equal(["Eigene"], sicht.Select(n => n.Betreff));
        Assert.Equal(1, await u.Messagerie.UngeleseneAsync(resp.Id));
        var fremdId = (await u.Messagerie.SucheAsync(admin.Id, Postfach.Eingang, "Fremde", null)).Eintraege.Single().Id;
        await Assert.ThrowsAsync<FachFehler>(() => u.Messagerie.OeffnenAsync(resp.Id, fremdId));

        // Antwort: gelesen, Ausgang mit « Re: » und « [SKOULOU] » genau einmal, Mail in der Outbox.
        var detail = await u.Messagerie.OeffnenAsync(resp.Id, sicht[0].Id);
        Assert.Equal(0, await u.Messagerie.UngeleseneAsync(resp.Id));
        await u.Messagerie.AntwortenAsync(resp.Id, detail.Id, "Bonjour, voici la réponse.");
        var antwort = (await u.Messagerie.SucheAsync(resp.Id, Postfach.Ausgang, null, null)).Eintraege.Single();
        Assert.Equal(("[SKOULOU] Re: Eigene", "dir@exemple.tg"), (antwort.Betreff, antwort.An));
        Assert.Equal("[SKOULOU] Re: Eigene", Assert.Single(await u.MailsAsync("dir@exemple.tg")).Betreff);
        Assert.Equal(("Re: x", "Re: Question"), (MessagerieService.Antwortbetreff("Re: x"), MessagerieService.Antwortbetreff("Question")));

        // Responsable schreibt nur an Konten seiner Schulen; Zuordnen nur der Admin.
        await Assert.ThrowsAsync<FachFehler>(() => u.Messagerie.NeuAsync(resp.Id, "dir@exemple.tg", "Objet", "Texte", null));
        await Assert.ThrowsAsync<FachFehler>(() => u.Messagerie.NeuAsync(resp.Id, "x@exemple.tg", "Objet", "Texte", eigene.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Messagerie.NeuAsync(resp.Id, "x@exemple.tg", "Objet", "Texte", fremde.Id));
        await u.Messagerie.NeuAsync(resp.Id, "dir@exemple.tg", "Objet", "Texte", eigene.Id);
        Assert.Single(await u.Messagerie.EmpfaengerAsync(resp.Id, eigene.Id));
        // Fehlersuche 21.11.2026 (R5), Nutzerentscheidung 22.11.: Schüler und Eltern der eigenen Schule sind keine Empfänger,
        // Koordination schon; die Meldung ist dieselbe wie bei einer fremden Adresse (verrät keine Zugehörigkeit).
        await u.Basis.KontoAsync("eleve", Rollen.Schueler, eigene.Id, email: "eleve@exemple.tg");
        await u.Basis.KontoAsync("parent", Rollen.Eltern, eigene.Id, email: "parent@exemple.tg");
        await u.Basis.KontoAsync("coord", Rollen.Koordinator, eigene.Id, email: "coord@exemple.tg");
        var schueler = await Assert.ThrowsAsync<FachFehler>(() => u.Messagerie.NeuAsync(resp.Id, "eleve@exemple.tg", "Objet", "Texte", eigene.Id));
        var unbekannt = await Assert.ThrowsAsync<FachFehler>(() => u.Messagerie.NeuAsync(resp.Id, "x@exemple.tg", "Objet", "Texte", eigene.Id));
        Assert.Equal(unbekannt.Message, schueler.Message);
        await Assert.ThrowsAsync<FachFehler>(() => u.Messagerie.NeuAsync(resp.Id, "parent@exemple.tg", "Objet", "Texte", eigene.Id));
        await u.Messagerie.NeuAsync(resp.Id, "coord@exemple.tg", "Objet", "Texte", eigene.Id);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.Messagerie.ZuordnenAsync(resp.Id, fremdId, eigene.Id));
        await u.Messagerie.ZuordnenAsync(admin.Id, fremdId, eigene.Id);
        Assert.Equal(2, (await u.Messagerie.SucheAsync(resp.Id, Postfach.Eingang, null, null)).Eintraege.Count);
    }

    [Fact]
    public async Task Webhook_Nachricht_eines_Direktors_landet_bei_seinem_Responsable()
    {
        await using var u = new PlattformUmgebung();
        await u.StartAsync();
        var resp = await u.Basis.KontoAsync("resp", Rollen.Responsable, email: "resp@exemple.tg");
        var schule = await u.Basis.SchuleAsync("Eigene", responsable: resp.Id);
        await u.Basis.KontoAsync("dir", Rollen.Direktor, schule.Id, email: "Dir@Exemple.tg");
        var eingang = new PostEingangService(u.Db, new MailWeiterleitung(new FakeHttp(System.Net.HttpStatusCode.OK),
                new TestOptionen<PostmarkOptionen>(new PostmarkOptionen()), NullLogger<MailWeiterleitung>.Instance),
            new HtmlBereiniger(), new TestOptionen<PostmarkOptionen>(new PostmarkOptionen()));
        var json = $$"""{"MessageID":"pm-42","From":"dir@exemple.tg","To":"{{SkoulouMarke.KontaktEmail}}","Subject":"Question","HtmlBody":"<p>Bonjour<script>x()</script></p>"}""";

        Assert.Equal(EingangsErgebnis.Gespeichert, await eingang.VerarbeiteAsync(json));
        Assert.Equal(EingangsErgebnis.Doppelt, await eingang.VerarbeiteAsync(json));
        var n = (await u.Messagerie.SucheAsync(resp.Id, Postfach.Eingang, null, null)).Eintraege.Single();
        Assert.Equal(schule.Id, n.SchuleId);
        Assert.DoesNotContain("script", (await u.Messagerie.OeffnenAsync(resp.Id, n.Id)).TextHtml);
    }

    [Fact]
    public async Task Aufbewahrung_Fehler_90_Tage_Mails_2_Jahre_Audit_unbegrenzt()
    {
        await using var u = new PlattformUmgebung();
        var jetzt = u.Zeit.Jetzt.UtcDateTime;
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            db.FehlerProtokolle.AddRange(new FehlerProtokoll { ZeitUtc = jetzt.AddDays(-91), Nachricht = "alt" }, new FehlerProtokoll { ZeitUtc = jetzt.AddDays(-89), Nachricht = "neu" });
            db.AuditEintraege.Add(new AuditEintrag { ZeitUtc = jetzt.AddYears(-5), Aktion = "schule.validiert", ZielTyp = "Schule", Text = "ancien", ErstelltAmUtc = jetzt.AddYears(-5) });
            db.MailAusgaenge.Add(new MailAusgang { An = "a@exemple.tg", Status = MailStatus.Offen, ErstelltAmUtc = jetzt.AddDays(-800) });
            db.Nachrichten.AddRange(
                new Nachricht { Von = "a@exemple.tg", Betreff = "alt", ErstelltAmUtc = jetzt.AddDays(-731) },
                new Nachricht { Von = "b@exemple.tg", Betreff = "neu", ErstelltAmUtc = jetzt.AddDays(-700) });
            await db.SaveChangesAsync();
        }
        // ErstelltAmUtc wird beim Speichern gesetzt – für den Test zurückdatieren.
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            foreach (var n in db.Nachrichten) n.ErstelltAmUtc = n.Betreff == "alt" ? jetzt.AddDays(-731) : jetzt.AddDays(-700);
            foreach (var m in db.MailAusgaenge) m.ErstelltAmUtc = jetzt.AddDays(-800);
            await db.SaveChangesAsync();
        }
        var (fehler, mails, nachrichten) = await new AufraeumDienst(u.Db, u.Zeit, NullLogger<AufraeumDienst>.Instance).BereinigeAsync();
        Assert.Equal((1, 0, 1), (fehler, mails, nachrichten)); // offene Mails bleiben, bis sie versendet sind
        Assert.Equal(1, await u.LiesAsync(db => db.AuditEintraege.CountAsync()));
        Assert.Equal("neu", (await u.LiesAsync(db => db.Nachrichten.SingleAsync())).Betreff);
        Assert.Equal(TimeSpan.FromDays(90), AufraeumDienst.FehlerAufbewahrung);
    }

    [Fact]
    public async Task Medien_nur_ungenutzte_loeschen()
    {
        await using var u = new PlattformUmgebung();
        var admin = await u.StartAsync();
        var schule = await u.Basis.SchuleAsync();
        var genutzt = new Medium { SchuleId = schule.Id, BesitzerId = admin.Id, BlobName = "schulen/a/logo/1", Dateiname = "logo.png", Bytes = 2000 };
        var frei = new Medium { SchuleId = schule.Id, BesitzerId = admin.Id, BlobName = "schulen/a/inhalt/2", Dateiname = "alt.png", Bytes = 5000 };
        await using (var db = await u.Db.CreateDbContextAsync())
        {
            db.Medien.AddRange(genutzt, frei);
            (await db.Schulen.SingleAsync()).LogoMediumId = genutzt.Id;
            await db.SaveChangesAsync();
        }
        u.Speicher.Dateien[frei.BlobName] = [1, 2, 3];

        var summen = await u.Medien.SummenAsync(admin.Id);
        Assert.Equal((2, 7000L, 1, 5000L), (summen.Anzahl, summen.Bytes, summen.Ungenutzt, summen.UngenutztBytes));
        Assert.Equal(["alt.png"], (await u.Medien.SucheAsync(admin.Id, null, MedienVerwaltungService.FilterUngenutzt, null)).Eintraege.Select(m => m.Dateiname));
        await Assert.ThrowsAsync<FachFehler>(() => u.Medien.LoeschenAsync(admin.Id, genutzt.Id));
        await u.Medien.LoeschenAsync(admin.Id, frei.Id);
        Assert.Empty(u.Speicher.Dateien);
        Assert.Equal(1, await u.LiesAsync(db => db.Medien.CountAsync()));
        Assert.True(await u.LiesAsync(db => db.AuditEintraege.AnyAsync(a => a.Aktion == PlattformAudit.MediumGeloescht)));
    }
}
