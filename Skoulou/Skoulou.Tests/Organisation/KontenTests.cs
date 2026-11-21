using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Konten der Schule: Anlage mit Startpasswort, Matrikel, Status nur über ausdrückliche Aktionen, Archiv, Freigabe.</summary>
public class KontenTests
{
    [Fact]
    public async Task Personal_anlegen_mit_Vorschlag_lesbarem_Startpasswort_und_Willkommensmail()
    {
        var w = await OrgWelt.ErstelleAsync();
        var stapel = await w.D.Personal.AnlegenAsync(w.Koordinator, new PersonalEingabe(Rollen.Lehrer, " Kofi ", "Annan", null, "Kofi.Annan@Exemple.tg", "+228 90 11 22 33", Geschlecht.Maennlich));
        var zugang = Assert.Single(stapel.Zugaenge);
        Assert.Equal("kofi.annan", zugang.Identifiant);
        Assert.Equal(8, zugang.Passwort.Length);
        Assert.True(StartPasswort.IstLesbar(zugang.Passwort));
        Assert.Same(stapel, w.D.Ablage.Holen(stapel.Id, w.Koordinator));
        Assert.Null(w.D.Ablage.Holen(stapel.Id, w.Direktor));

        var zweiter = await w.D.Personal.AnlegenAsync(w.Koordinator, new PersonalEingabe(Rollen.Lehrer, "Kofi", "Annan", null, null, null, Geschlecht.Maennlich));
        Assert.Equal("kofi.annan2", zweiter.Zugaenge[0].Identifiant);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Personal.AnlegenAsync(w.Koordinator, new PersonalEingabe(Rollen.Lehrer, "Ama", "Mensah", "kofi.annan", null, null, Geschlecht.Weiblich)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Personal.AnlegenAsync(w.Koordinator, new PersonalEingabe(Rollen.Lehrer, "Ama", "Mensah", null, "pas-une-adresse", null, Geschlecht.Weiblich)));

        var konto = await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == zugang.KontoId));
        Assert.True(konto.MussPasswortAendern);
        Assert.Equal(KontoStatus.Actif, konto.Status);
        Assert.Equal(w.SchuleA, konto.SchuleId);
        Assert.Equal("kofi.annan@exemple.tg", konto.Email);
        Assert.Equal(w.Koordinator, konto.ErstelltVonId);
        // Nur das Konto mit Adresse bekommt eine Mail (ohne Passwort).
        var mails = await w.MitDbAsync(db => db.MailAusgaenge.ToListAsync());
        var mail = Assert.Single(mails);
        Assert.Equal("kofi.annan@exemple.tg", mail.An);
        Assert.DoesNotContain(zugang.Passwort, mail.Html);
        Assert.Equal(2, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.KontoAngelegt)));
    }

    [Fact]
    public async Task Matrikel_automatisch_lesbar_eindeutig_auch_manuell()
    {
        var w = await OrgWelt.ErstelleAsync();
        var erster = await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Afi", "Koffi"), w.Klasse6A);
        var detail = await w.D.Schueler.DetailAsync(w.Direktor, erster.Zugaenge[0].KontoId);
        Assert.Equal("CA260004", detail.Daten.Matrikel);
        Assert.Matches("^CA26[0-9]{4}$", detail.Daten.Matrikel!);
        Assert.Equal("6e A", detail.Aktive!.Klasse);
        Assert.Equal(OrgWelt.Heute, detail.Aktive.Von);   // Standard einer schon vor dem Jahr angemeldeten Schule: heute (21.11.2026)
        Assert.Equal("6e A", erster.Zugaenge[0].Zusatz);

        // Manuell vergebene Nummer wird von der Automatik übersprungen; manuelle Dubletten werden abgewiesen.
        await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Yawa", "Tchalla", " ca260005 "), null);
        Assert.Equal("matrikel", (await Assert.ThrowsAsync<FachFehler>(() =>
            w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Edem", "Kodjo", "CA260001"), null))).Feld);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Edem", "Kodjo", "CA 26#1"), null));
        var dritter = await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Edem", "Kodjo"), null);
        Assert.Equal("CA260006", (await w.D.Schueler.DetailAsync(w.Direktor, dritter.Zugaenge[0].KontoId)).Daten.Matrikel);
        Assert.Null((await w.D.Schueler.DetailAsync(w.Direktor, dritter.Zugaenge[0].KontoId)).Aktive);

        // Ändern auf eine vergebene Matrikel scheitert, auf eine freie gelingt.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schueler.AendernAsync(w.Direktor, w.SchuelerZiel, OrgWelt.SchuelerDaten("Test", "Ziel", "CA260001")));
        await w.D.Schueler.AendernAsync(w.Direktor, w.SchuelerZiel, OrgWelt.SchuelerDaten("Test", "Ziel", "EXT-77"));
        Assert.Equal("EXT-77", (await w.D.Schueler.DetailAsync(w.Direktor, w.SchuelerZiel)).Daten.Matrikel);
    }

    [Fact]
    public async Task Matrikel_ohne_Praefix_aus_dem_Schulnamen()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.MitDbAsync(async db =>
        {
            db.Schulen.Single(s => s.Id == w.SchuleA).MatrikelPraefix = null;
            return await db.SaveChangesAsync();
        });
        var z = await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Afi", "Koffi"), null);
        var matrikel = (await w.D.Schueler.DetailAsync(w.Direktor, z.Zugaenge[0].KontoId)).Daten.Matrikel!;
        Assert.StartsWith(NummernFormat.PraefixAusName(w.SchulNameA) + "26", matrikel);
    }

    [Fact]
    public async Task Bearbeiten_aendert_nie_den_Kontostatus()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Konten.SperrenAsync(w.Direktor, w.SchuelerZiel);
        await w.D.Schueler.AendernAsync(w.Direktor, w.SchuelerZiel, OrgWelt.SchuelerDaten("Neu", "Nom"));
        await w.D.Schueler.AendernAsync(w.Direktor, w.SchuelerWartend, OrgWelt.SchuelerDaten("Attente", "Toujours"));
        await w.D.Konten.SperrenAsync(w.Direktor, w.LehrerZiel);
        await w.D.Personal.AendernAsync(w.Direktor, w.LehrerZiel, new PersonalEingabe(Rollen.Lehrer, "Prof", "Bloqué", null, null, null, Geschlecht.Divers));
        await w.D.Konten.SperrenAsync(w.Direktor, w.ElternZiel);
        await w.D.Eltern.AendernAsync(w.Direktor, w.ElternZiel, new ElternEingabe("Parent", "Bloqué", null, null, null, Geschlecht.Divers));

        var status = await w.MitDbAsync(db => db.Users.Where(k => new[] { w.SchuelerZiel, w.SchuelerWartend, w.LehrerZiel, w.ElternZiel }.Contains(k.Id))
            .ToDictionaryAsync(k => k.Id, k => k.Status));
        Assert.Equal(KontoStatus.Bloque, status[w.SchuelerZiel]);
        Assert.Equal(KontoStatus.EnAttente, status[w.SchuelerWartend]);
        Assert.Equal(KontoStatus.Bloque, status[w.LehrerZiel]);
        Assert.Equal(KontoStatus.Bloque, status[w.ElternZiel]);
        Assert.Equal("Neu", (await w.D.Schueler.DetailAsync(w.Direktor, w.SchuelerZiel)).Daten.Vorname);

        // Rolle ändert sich nie beim Bearbeiten.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Personal.AendernAsync(w.Direktor, w.LehrerZiel,
            new PersonalEingabe(Rollen.Koordinator, "Prof", "Bloqué", null, null, null, Geschlecht.Divers)));
        // Selbstregistrierung kann nicht gesperrt/archiviert werden – nur validiert oder abgelehnt.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.SperrenAsync(w.Direktor, w.SchuelerWartend));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.ArchivierenAsync(w.Direktor, w.SchuelerWartend));
    }

    [Fact]
    public async Task Sperren_Entsperren_Archivieren_Reintegrieren_mit_Audit()
    {
        var w = await OrgWelt.ErstelleAsync();
        var sitzungVorher = await w.MitDbAsync(db => db.Users.Where(k => k.Id == w.LehrerZiel).Select(k => k.AktiveSitzungId).SingleAsync());
        await w.D.Konten.SperrenAsync(w.Direktor, w.LehrerZiel);
        var gesperrt = await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == w.LehrerZiel));
        Assert.Equal(KontoStatus.Bloque, gesperrt.Status);
        Assert.NotEqual(sitzungVorher, gesperrt.AktiveSitzungId);
        await w.D.Konten.EntsperrenAsync(w.Direktor, w.LehrerZiel);
        Assert.Equal(KontoStatus.Actif, (await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == w.LehrerZiel))).Status);

        // Schüler mit aktiver Einschreibung: erst « Départ », dann archivieren.
        var fehler = await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.ArchivierenAsync(w.Direktor, w.SchuelerZiel));
        Assert.Contains("Départ", fehler.Message);
        await w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 11, 12), "Déménagement à Kara", archivieren: false);
        await w.D.Konten.ArchivierenAsync(w.Direktor, w.SchuelerZiel);
        Assert.Equal(KontoStatus.Archive, (await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == w.SchuelerZiel))).Status);
        Assert.Equal(1, (await w.D.Schueler.ZaehlerAsync(w.Direktor)).Archives);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.EntsperrenAsync(w.Direktor, w.SchuelerZiel));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.PasswortZuruecksetzenAsync(w.Direktor, [w.SchuelerZiel]));

        await w.D.Konten.ReintegrierenAsync(w.Direktor, w.SchuelerZiel);
        Assert.Equal(KontoStatus.Actif, (await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == w.SchuelerZiel))).Status);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.ReintegrierenAsync(w.Direktor, w.SchuelerZiel));

        var audit = await w.MitDbAsync(db => db.AuditEintraege.Select(a => a.Aktion).ToListAsync());
        foreach (var aktion in new[] { AuditAktionen.KontoGesperrt, OrgAudit.KontoEntsperrt, AuditAktionen.KontoArchiviert, OrgAudit.KontoReintegriert, OrgAudit.Austritt })
            Assert.Contains(aktion, audit);
    }

    [Fact]
    public async Task Passwort_zuruecksetzen_fuer_mehrere_Konten_beendet_Sitzungen()
    {
        var w = await OrgWelt.ErstelleAsync();
        var stapel = await w.D.Konten.PasswortZuruecksetzenAsync(w.Koordinator, [w.Schueler, w.Eltern, w.LehrerZiel, w.Schueler]);
        Assert.Equal(3, stapel.Zugaenge.Count);
        Assert.All(stapel.Zugaenge, z => Assert.True(StartPasswort.IstLesbar(z.Passwort)));
        Assert.Equal("6e A", stapel.Zugaenge.Single(z => z.KontoId == w.Schueler).Zusatz);
        var konten = await w.MitDbAsync(db => db.Users.Where(k => k.Id == w.Schueler || k.Id == w.Eltern || k.Id == w.LehrerZiel).ToListAsync());
        Assert.All(konten, k =>
        {
            Assert.True(k.MussPasswortAendern);
            Assert.NotNull(k.AktiveSitzungId);
            Assert.NotNull(k.PasswordHash);
        });
        Assert.Equal(3, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.PasswortZurueckgesetzt)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Konten.PasswortZuruecksetzenAsync(w.Direktor, []));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Konten.PasswortZuruecksetzenAsync(w.Direktor, [w.Schueler, w.DirektorB]));
        // Keine Mail beim Zurücksetzen.
        Assert.Equal(0, await w.MitDbAsync(db => db.MailAusgaenge.CountAsync()));
    }

    [Fact]
    public async Task Selbstregistrierung_validieren_schreibt_in_einem_Schritt_ein()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.MitDbAsync(async db =>
        {
            db.Users.Single(k => k.Id == w.SchuelerWartend).Email = "eleve@exemple.tg";
            return await db.SaveChangesAsync();
        });
        var zaehler = await w.D.Schueler.ZaehlerAsync(w.Direktor);
        Assert.Equal(1, zaehler.AValider);
        Assert.Single((await w.D.Schueler.SucheAsync(w.Direktor, SchuelerService.FilterAValider, null, null, 0, 50)).Eintraege);

        await w.D.Schueler.ValidierenAsync(w.Koordinator, w.SchuelerWartend, w.Klasse6B, wiederholer: true);
        var detail = await w.D.Schueler.DetailAsync(w.Direktor, w.SchuelerWartend);
        Assert.Equal(KontoStatus.Actif, detail.Status);
        Assert.Equal("6e B", detail.Aktive!.Klasse);
        Assert.True(detail.Aktive.Wiederholer);
        Assert.Equal("CA260004", detail.Daten.Matrikel);
        Assert.NotNull(await w.MitDbAsync(db => db.SchuelerProfile.Where(p => p.KontoId == w.SchuelerWartend).Select(p => p.AufnahmeBestaetigtAmUtc).SingleAsync()));
        Assert.Contains("activé", (await w.MitDbAsync(db => db.MailAusgaenge.SingleAsync())).Betreff);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.InscriptionValidiert)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schueler.ValidierenAsync(w.Direktor, w.SchuelerWartend, w.Klasse6A, false));
        Assert.Equal(0, (await w.D.Schueler.ZaehlerAsync(w.Direktor)).AValider);
    }

    [Fact]
    public async Task Selbstregistrierung_ablehnen_loescht_Konto_ohne_Schuldaten()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Schueler.AblehnenAsync(w.Direktor, w.SchuelerWartend, "Inconnu de l'établissement");
        Assert.False(await w.MitDbAsync(db => db.Users.AnyAsync(k => k.Id == w.SchuelerWartend)));
        Assert.False(await w.MitDbAsync(db => db.SchuelerProfile.AnyAsync(p => p.KontoId == w.SchuelerWartend)));
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.InscriptionAbgelehnt)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schueler.AblehnenAsync(w.Direktor, w.Schueler, null));
    }

    [Fact]
    public async Task Eltern_anlegen_verknuepfen_trennen_hoechstens_zwoelf_Kinder()
    {
        var w = await OrgWelt.ErstelleAsync();
        var vor = await w.D.Eltern.VorbelegungAsync(w.Direktor, w.Schueler);
        Assert.Equal("eleve", vor.Nachname);
        var stapel = await w.D.Eltern.AnlegenAsync(w.Direktor, new ElternEingabe("Kodzo", "Adjo", null, null, "+228 91 00 00 00", Geschlecht.Maennlich), w.Schueler, "père");
        var neu = stapel.Zugaenge[0];
        Assert.Equal("Père de Test eleve", neu.Zusatz);
        await w.D.Eltern.VerknuepfenAsync(w.Direktor, neu.KontoId, w.SchuelerZiel, "Tuteur");
        await w.D.Eltern.VerknuepfenAsync(w.Direktor, neu.KontoId, w.SchuelerZiel, "Autre");
        var detail = await w.D.Eltern.DetailAsync(w.Direktor, neu.KontoId);
        Assert.Equal(2, detail.Kinder.Count);
        Assert.Equal("Autre", detail.Kinder.Single(k => k.SchuelerId == w.SchuelerZiel).Beziehung);
        Assert.Equal("6e A", detail.Kinder[0].Klasse);

        // Suche über den Namen eines Kindes oder seine Matrikel.
        Assert.Contains((await w.D.Eltern.SucheAsync(w.Direktor, null, "CA260002", 0, 50)).Eintraege, e => e.KontoId == neu.KontoId);
        await w.D.Eltern.TrennenAsync(w.Direktor, neu.KontoId, w.SchuelerZiel);
        Assert.Single((await w.D.Eltern.DetailAsync(w.Direktor, neu.KontoId)).Kinder);
        Assert.Equal(1, (await w.D.Eltern.ZaehlerAsync(w.Direktor)).OhneKind);

        await Assert.ThrowsAsync<FachFehler>(() => w.D.Eltern.VerknuepfenAsync(w.Direktor, neu.KontoId, w.Lehrer, "Mère"));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Eltern.VerknuepfenAsync(w.Direktor, neu.KontoId, w.DirektorB, "Mère"));
        await w.MitDbAsync(async db =>
        {
            for (var i = 0; i < 11; i++)
                db.ElternKinder.Add(new ElternKind { SchuleId = w.SchuleA, ElternId = neu.KontoId, SchuelerId = Guid.NewGuid() });
            return await db.SaveChangesAsync();
        });
        Assert.Contains("12", (await Assert.ThrowsAsync<FachFehler>(() => w.D.Eltern.VerknuepfenAsync(w.Direktor, neu.KontoId, w.SchuelerOhne, "Mère"))).Message);
    }

    [Fact]
    public async Task Foto_und_Kartencode()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Schueler.FotoHochladenAsync(w.Direktor, w.Schueler, new MemoryStream([1, 2, 3, 4]), "x.png"));
        var medium = await w.D.Schueler.FotoHochladenAsync(w.Direktor, w.Schueler, new MemoryStream(OrgWelt.Png), "photo.png");
        Assert.Equal(medium, (await w.D.Schueler.DetailAsync(w.Direktor, w.Schueler)).FotoMediumId);
        Assert.Single(w.D.Speicher.Dateien);
        await w.D.Schueler.KartenCodeErneuernAsync(w.Direktor, w.Schueler);
        var token1 = await w.MitDbAsync(db => db.SchuelerProfile.Where(p => p.KontoId == w.Schueler).Select(p => p.KartenToken).SingleAsync());
        await w.D.Schueler.KartenCodeErneuernAsync(w.Direktor, w.Schueler);
        var token2 = await w.MitDbAsync(db => db.SchuelerProfile.Where(p => p.KontoId == w.Schueler).Select(p => p.KartenToken).SingleAsync());
        Assert.Matches("^[0-9a-f]{32}$", token1!);
        Assert.NotEqual(token1, token2);
        await w.D.Schueler.FotoEntfernenAsync(w.Direktor, w.Schueler);
        Assert.Null((await w.D.Schueler.DetailAsync(w.Direktor, w.Schueler)).FotoMediumId);
    }
}
