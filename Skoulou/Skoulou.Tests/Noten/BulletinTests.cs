using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Conseil, Veröffentlichung, Nummern, Rectification, Rückzug, Übergaben, Lesen, QR-Prüfung, PDF, Klassenwechsel.</summary>
public class BulletinTests
{
    [Fact]
    public async Task Uebersicht_listet_fehlende_Noten_und_Non_classe()
    {
        var w = await NotenWelt.ErstelleAsync();
        var math = await w.BewertungAsync(w.O.Kf6AMath);
        await w.NoteAsync(math, w.E1, "14");
        var u = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(2, u.Effectif);
        var fehlend = Assert.Single(u.Fehlend);
        Assert.Equal("Mathématiques", fehlend.Fach);
        Assert.Equal(1, u.NonClasses);
        Assert.Equal("1er", u.Zeilen.Single(z => z.EinschreibungId == w.E1).Rang.Text);
        Assert.False(u.IstLetztePeriode);
        Assert.Contains("incomplètes", (await Assert.ThrowsAsync<FachFehler>(() => w.VeroeffentlichenAsync())).Message);
        var ergebnis = await w.VeroeffentlichenAsync(trotz: true);
        Assert.Equal(2, ergebnis.Bulletins);
    }

    [Fact]
    public async Task Klassenwechsel_rueckdatiert_in_veroeffentlichte_Periode_wird_abgewiesen()
    {
        // Klicktest 21.11.2026: T1 der 6e A veröffentlicht, Wechsel nach 6e B zum 01.12. → zwei gültige T1-Bulletins.
        var w = await NotenWelt.ErstelleAsync();
        await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync();
        var f = await Assert.ThrowsAsync<FachFehler>(() => w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.Schueler, w.O.Klasse6B, new DateOnly(2026, 12, 1)));
        Assert.Contains("déjà publiés", f.Message);
        await w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.Schueler, w.O.Klasse6B, new DateOnly(2027, 1, 5));   // nach dem Periodenende
    }

    [Fact]
    public async Task Veroeffentlichung_vergibt_Nummern_Token_Mails_und_sperrt()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.VollstaendigAsync();
        await w.AendernAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == w.O.Eltern)).Email = "parent@exemple.tg";
            (await db.SchuelerProfile.SingleAsync(p => p.KontoId == w.O.Schueler)).ElternEmail = "PARENT@exemple.tg";
        });
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.VeroeffentlichenAsync(akteur: w.O.Lehrer));
        var ergebnis = await w.VeroeffentlichenAsync(akteur: w.O.Koordinator);
        Assert.Equal((2, 1), (ergebnis.Bulletins, ergebnis.Mails)); // doppelte Adresse nur einmal

        var bulletins = await w.MitDbAsync(db => db.Bulletins.OrderBy(b => b.Nummer).ToListAsync());
        Assert.Equal(["BUL-2026-00001", "BUL-2026-00002"], bulletins.Select(b => b.Nummer));
        Assert.All(bulletins, b => Assert.Matches("^[0-9a-f]{64}$", b.PruefToken));
        Assert.All(bulletins, b => Assert.Equal(64, b.Hash.Length));
        Assert.DoesNotContain("\"Logo\"", bulletins[0].SnapshotJson);
        var snapshot = BulletinSnapshot.AusJson(bulletins.Single(b => b.EinschreibungId == w.E1).SnapshotJson);
        Assert.Equal("13,00", snapshot.Inhalt.MoyenneText);
        Assert.Equal("1er", snapshot.Inhalt.Rang);
        Assert.Equal(Distinktion.TableauHonneur, snapshot.Inhalt.Distinktion);
        Assert.Equal(1, await w.MitDbAsync(db => db.MailAusgaenge.CountAsync()));
        Assert.Equal(1, await w.MitDbAsync(db => db.BulletinUebergaben.CountAsync(u => u.Art == UebergabeArt.Notification)));
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.Veroeffentlicht)));

        await Assert.ThrowsAsync<FachFehler>(() => w.VeroeffentlichenAsync());
        var u = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(VeroeffentlichungsStatus.Publiee, u.Status);
        Assert.NotNull(u.ConseilSperre);
        await Assert.ThrowsAsync<FachFehler>(() => w.Conseil.ConseilSpeichernAsync(w.D, w.O.P1, w.E1, new ConseilEingabe(Distinktion.Felicitations, Sanktionen.Keine, null)));
    }

    [Fact]
    public async Task Geaenderte_Daten_nach_der_Pruefung_verhindern_die_Veroeffentlichung()
    {
        var w = await NotenWelt.ErstelleAsync();
        var (math, _) = await w.VollstaendigAsync();
        var u = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        await w.NoteAsync(math, w.E1, "16");
        Assert.Contains("changé", (await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.VeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, u.KlassenHash, false))).Message);
        await w.Conseil.ConseilSpeichernAsync(w.D, w.O.P1, w.E2, new ConseilEingabe(null, Sanktionen.AvertissementConduite, "Bavardages"));
        var neu = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.NotEqual(u.KlassenHash, neu.KlassenHash);
        await w.Bulletins.VeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, neu.KlassenHash, false);
        var b = BulletinSnapshot.AusJson(await w.MitDbAsync(db => db.Bulletins.Where(x => x.EinschreibungId == w.E2).Select(x => x.SnapshotJson).SingleAsync()));
        Assert.Equal(["Avertissement conduite"], b.Inhalt.SanktionenTexte);
        Assert.Equal("Bavardages", b.Inhalt.ConseilBemerkung);
    }

    [Fact]
    public async Task Conseil_Rechte_Professeur_principal_bereitet_vor()
    {
        var w = await NotenWelt.ErstelleAsync();
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Conseil.UebersichtAsync(w.O.Lehrer, w.O.Klasse6A, w.O.P1));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Conseil.UebersichtAsync(w.O.Schueler, w.O.Klasse6A, w.O.P1));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Conseil.UebersichtAsync(w.O.DirektorB, w.O.Klasse6A, w.O.P1));
        await w.AendernAsync(async db => (await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse6A)).KlassenlehrerId = w.O.Lehrer);
        var u = await w.Conseil.UebersichtAsync(w.O.Lehrer, w.O.Klasse6A, w.O.P1);
        Assert.False(u.Direktion);
        await w.Conseil.ConseilSpeichernAsync(w.O.Lehrer, w.O.P1, w.E1, new ConseilEingabe(Distinktion.Encouragements, Sanktionen.Keine, "Très bon trimestre"));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bulletins.VeroeffentlichenAsync(w.O.Lehrer, w.O.Klasse6A, w.O.P1, u.KlassenHash, true));
        Assert.Single((await w.Conseil.AuswahlAsync(w.O.Lehrer, w.O.Jahr)).Klassen);
        await w.Conseil.ConseilSpeichernAsync(w.D, w.O.P1, w.E1, new ConseilEingabe(null, Sanktionen.Keine, null));
        Assert.Equal(0, await w.MitDbAsync(db => db.KlassenratEintraege.CountAsync()));
    }

    [Fact]
    public async Task Rectification_ersetzt_nur_geaenderte_Bulletins()
    {
        var w = await NotenWelt.ErstelleAsync();
        var (math, _) = await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync();
        var alt = await w.MitDbAsync(db => db.Bulletins.SingleAsync(b => b.EinschreibungId == w.E2));

        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.RectificationVorschauAsync(w.D, w.O.Klasse6A, w.O.P1));
        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, " "));
        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Erreur de report en mathématiques");
        await w.Bulletins.RectificationAbbrechenAsync(w.D, w.O.Klasse6A, w.O.P1); // ohne Änderung erlaubt
        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Erreur de report en mathématiques");
        await w.NoteAsync(math, w.E2, "19"); // eleveziel: (19·3 + 14·2)/5 = 17 → wird 1er, eleve rutscht auf 2e
        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.RectificationAbbrechenAsync(w.D, w.O.Klasse6A, w.O.P1));
        var vorschau = await w.Bulletins.RectificationVorschauAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(2, vorschau.Aenderungen.Count); // auch der Rang des Mitschülers ändert sich
        var ziel = vorschau.Aenderungen.Single(a => a.EinschreibungId == w.E2);
        Assert.Equal(("12,20", "17,00", "2e", "1er"), (ziel.AltMoyenne, ziel.NeuMoyenne, ziel.AltRang, ziel.NeuRang));
        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.RectificationVeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, "faux"));
        Assert.Equal(2, await w.Bulletins.RectificationVeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, vorschau.Hash));

        var neu = await w.MitDbAsync(db => db.Bulletins.SingleAsync(b => b.EinschreibungId == w.E2 && b.Status == BulletinStatus.Publie));
        Assert.True(neu.IstRectificatif);
        Assert.Equal(alt.Id, neu.ErsetztId);
        Assert.Equal("BUL-2026-00004", (await w.MitDbAsync(db => db.Bulletins.OrderByDescending(b => b.Nummer).FirstAsync())).Nummer);
        Assert.Equal(BulletinStatus.Remplace, (await w.MitDbAsync(db => db.Bulletins.SingleAsync(b => b.Id == alt.Id))).Status);
        Assert.Equal(BulletinStatus.Remplace, (await w.Lesen.PruefenAsync(alt.PruefToken))!.Status);
        Assert.Equal("17,00", (await w.Lesen.PruefenAsync(neu.PruefToken))!.Moyenne);
        Assert.Equal(VeroeffentlichungsStatus.Publiee, (await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1)).Status);
        Assert.Contains("remplace", BulletinSnapshot.AusJson(neu.SnapshotJson).Dokument!.ErsetztNummer is { } n ? "remplace " + n : "");
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.Berichtigt)));
    }

    /// <summary>
    /// 21.11.2026: eine Rectification gibt nur Bulletins neu aus, die sich fachlich geändert haben. Vorlage, Kopf der Schule und
    /// Namen der Lehrkräfte zählen nicht – sonst sahen Eltern unbeteiligter Schüler « Rectifié ».
    /// </summary>
    [Fact]
    public async Task Rectification_gibt_nur_fachlich_geaenderte_Bulletins_neu_aus()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync();
        var altE1 = await w.MitDbAsync(db => db.Bulletins.SingleAsync(b => b.EinschreibungId == w.E1));
        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Appréciation oubliée");

        // Nicht fachlich: neue Vorlage, neue Adresse der Schule, anderer Name der Lehrkraft → keine Wirkung.
        await w.AendernAsync(async db =>
        {
            db.BulletinVorlagen.Add(new BulletinVorlage { SchuleId = w.O.SchuleA, Farbe = "#b91c1c", SchriftGroesse = 11 });
            (await db.Schulen.SingleAsync(s => s.Id == w.O.SchuleA)).Adresse = "Boulevard du 13 janvier, Lomé";
            (await db.Users.SingleAsync(u => u.Id == w.O.Lehrer)).Nachname = "Nouveau-Nom";
        });
        Assert.Empty((await w.Bulletins.RectificationVorschauAsync(w.D, w.O.Klasse6A, w.O.P1)).Aenderungen);
        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.RectificationVeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, "egal"));

        // Fachlich: Appréciation eines Schülers → nur sein Bulletin.
        await w.Noten.BemerkungSpeichernAsync(w.D, w.O.Kf6AMath, w.O.P1, w.E2, "Nette progression, continuez.");
        var vorschau = await w.Bulletins.RectificationVorschauAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(w.E2, Assert.Single(vorschau.Aenderungen).EinschreibungId);
        Assert.Equal(1, await w.Bulletins.RectificationVeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, vorschau.Hash));

        var e1 = await w.MitDbAsync(db => db.Bulletins.Where(b => b.EinschreibungId == w.E1).ToListAsync());
        Assert.Equal(altE1.Id, Assert.Single(e1).Id);                     // unverändert gültig, kein « Rectifié »
        Assert.Equal((BulletinStatus.Publie, false), (e1[0].Status, e1[0].IstRectificatif));
        Assert.False(Assert.Single(await w.Lesen.EigeneAsync(w.O.Eltern, w.O.Schueler)).IstRectificatif);
        var e2 = await w.MitDbAsync(db => db.Bulletins.SingleAsync(b => b.EinschreibungId == w.E2 && b.Status == BulletinStatus.Publie));
        Assert.True(e2.IstRectificatif);
        Assert.Equal("Nette progression, continuez.", BulletinSnapshot.AusJson(e2.SnapshotJson).Inhalt.Faecher.Single(f => f.Name == "Mathématiques").Appreciation);
    }

    /// <summary>Rang und Klassenstatistik der Mitschüler zählen – die Statistik aber nur, wenn die Vorlage sie druckt.</summary>
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task Klassenstatistik_der_Mitschueler_zaehlt_wenn_gedruckt(bool statistikGedruckt, int erwartet)
    {
        var w = await NotenWelt.ErstelleAsync();
        if (!statistikGedruckt)
            await w.AendernAsync(db =>
            {
                db.BulletinVorlagen.Add(new BulletinVorlage { SchuleId = w.O.SchuleA, ZeigeKlassenStatistik = false });
                return Task.CompletedTask;
            });
        var (math, _) = await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync();
        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Erreur de report");
        // eleveziel 11 → 12 in Mathématiques: (12·3 + 14·2)/5 = 12,80 – Ränge bleiben (eleve 13,00 1er), die Moyenne der Classe ändert sich.
        await w.NoteAsync(math, w.E2, "12");
        var vorschau = await w.Bulletins.RectificationVorschauAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(erwartet, vorschau.Aenderungen.Count);
        Assert.Contains(vorschau.Aenderungen, a => a.EinschreibungId == w.E2 && a.NeuMoyenne == "12,80");
        Assert.All(vorschau.Aenderungen, a => Assert.Equal(a.AltRang, a.NeuRang));
    }

    [Fact]
    public async Task Rueckzug_Uebergaben_und_Sicht_der_Familie()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync();
        var b = await w.MitDbAsync(db => db.Bulletins.SingleAsync(x => x.EinschreibungId == w.E1));

        Assert.Single(await w.Lesen.EigeneAsync(w.O.Schueler, w.O.Schueler));
        Assert.Single(await w.Lesen.EigeneAsync(w.O.Eltern, w.O.Schueler));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Lesen.EigeneAsync(w.O.ElternZiel, w.O.Schueler));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Lesen.EigeneAsync(w.O.Schueler, w.O.SchuelerZiel));
        Assert.NotNull(await w.Lesen.DruckAsync(w.O.Eltern, b.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Lesen.DruckAsync(w.O.SchuelerZiel, b.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Lesen.DruckAsync(w.O.Lehrer, b.Id));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Lesen.DruckAsync(w.O.DirektorB, b.Id));

        await w.Bulletins.UebergabeErfassenAsync(w.D, b.Id, UebergabeArt.Remise, "Remis au père en main propre");
        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.UebergabeErfassenAsync(w.D, b.Id, UebergabeArt.Remise, ""));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bulletins.UebergabenAsync(w.O.Schueler, b.Id));
        Assert.Contains(await w.Bulletins.UebergabenAsync(w.D, b.Id), u => u.Art == UebergabeArt.Remise);

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bulletins.ZurueckziehenAsync(w.O.Lehrer, b.Id, "x"));
        await w.Bulletins.ZurueckziehenAsync(w.D, b.Id, "Élève parti");
        Assert.Empty(await w.Lesen.EigeneAsync(w.O.Schueler, w.O.Schueler));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Lesen.DruckAsync(w.O.Schueler, b.Id));
        Assert.Equal(BulletinStatus.Retire, (await w.Lesen.PruefenAsync(b.PruefToken))!.Status);
        await Assert.ThrowsAsync<FachFehler>(() => w.Bulletins.ZurueckziehenAsync(w.D, b.Id, "encore"));
        var (liste, _) = await w.Lesen.SucheAsync(w.D, w.O.Jahr, null, null, null, "retire", 0);
        Assert.Single(liste);
    }

    [Fact]
    public async Task Pruefseite_zeigt_nur_die_vorgesehenen_Felder()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync();
        Assert.Null(await w.Lesen.PruefenAsync("abc"));
        Assert.Null(await w.Lesen.PruefenAsync(new string('0', 64)));
        var token = await w.MitDbAsync(db => db.Bulletins.Where(b => b.EinschreibungId == w.E1).Select(b => b.PruefToken).SingleAsync());
        var p = (await w.Lesen.PruefenAsync(token.ToUpperInvariant()))!;
        Assert.Equal(BulletinStatus.Publie, p.Status);
        Assert.Equal("Test eleve", p.Schueler);
        Assert.StartsWith("BUL-", p.Nummer);
        Assert.False(string.IsNullOrWhiteSpace(p.Datum));
        Assert.Null(p.ErsetztNummer);
        Assert.Equal(["Schule", "Schueler", "Klasse", "Periode", "Moyenne", "Status", "Nummer", "Datum", "ErsetztNummer"],
            typeof(BulletinPruefung).GetProperties().Where(x => x.Name != "EqualityContract").Select(x => x.Name));
    }

    [Fact]
    public async Task Pdf_einzeln_Klasse_Entwurf_und_Vorlage()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.VollstaendigAsync();
        var entwurf = await w.Pdf.EntwurfAsync(w.D, w.O.Klasse6A, w.O.P1, w.E1);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(entwurf.Inhalt, 0, 4));
        await w.VeroeffentlichenAsync();
        var id = await w.MitDbAsync(db => db.Bulletins.Where(b => b.EinschreibungId == w.E1).Select(b => b.Id).SingleAsync());
        var einzeln = await w.Pdf.BulletinAsync(w.O.Schueler, id, "https://localhost");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(einzeln.Inhalt, 0, 4));
        var klasse = await w.Pdf.KlasseAsync(w.D, w.O.Klasse6A, w.O.P1, "https://localhost");
        Assert.True(klasse.Inhalt.Length > einzeln.Inhalt.Length);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Pdf.KlasseAsync(w.O.Lehrer, w.O.Klasse6A, w.O.P1, "https://localhost"));
        await Assert.ThrowsAsync<FachFehler>(() => w.Pdf.KlasseAsync(w.D, w.O.Klasse6A, w.O.P2, "https://localhost"));
        var vorlage = await w.Pdf.VorlageAsync(w.D, null);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(vorlage.Inhalt, 0, 4));
    }

    [Fact]
    public async Task Vorlagen_je_Schule_und_Stufe()
    {
        var w = await NotenWelt.ErstelleAsync();
        var v = await w.Vorlagen.VorlageAsync(w.D, null);
        v.Name = "Modèle collège";
        v.Farbe = "#0E7490";
        v.ZeigeFoto = true;
        var id = await w.Vorlagen.SpeichernAsync(w.D, v);
        var doppelt = await w.Vorlagen.VorlageAsync(w.D, null);
        await Assert.ThrowsAsync<FachFehler>(() => w.Vorlagen.SpeichernAsync(w.D, doppelt));
        doppelt.StufeId = w.O.Stufe6;
        doppelt.SchriftGroesse = 13;
        await Assert.ThrowsAsync<FachFehler>(() => w.Vorlagen.SpeichernAsync(w.D, doppelt));
        doppelt.SchriftGroesse = 9;
        doppelt.Farbe = "rouge";
        await Assert.ThrowsAsync<FachFehler>(() => w.Vorlagen.SpeichernAsync(w.D, doppelt));
        doppelt.Farbe = "#b91c1c";
        await w.Vorlagen.SpeichernAsync(w.D, doppelt);
        Assert.Equal(2, (await w.Vorlagen.VorlagenAsync(w.O.Koordinator)).Count);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Vorlagen.SpeichernAsync(w.O.Lehrer, v));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Vorlagen.VorlageAsync(w.O.DirektorB, id));

        await w.VollstaendigAsync();
        var u = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        await w.Bulletins.VeroeffentlichenAsync(w.D, w.O.Klasse6A, w.O.P1, u.KlassenHash, false);
        var s = BulletinSnapshot.AusJson(await w.MitDbAsync(db => db.Bulletins.Select(b => b.SnapshotJson).FirstAsync()));
        Assert.Equal("#b91c1c", s.Inhalt.Layout.Farbe); // Stufenvorlage vor Standardvorlage
        Assert.Equal(9, s.Inhalt.Layout.SchriftGroesse);
    }

    [Fact]
    public async Task Klassenwechsel_Bulletin_der_neuen_Klasse_uebernimmt_Noten_desselben_Fachs()
    {
        var w = await NotenWelt.ErstelleAsync();
        var a = await w.BewertungAsync(w.O.Kf6AMath, titel: "Octobre", datum: new DateOnly(2026, 10, 1));
        await w.NoteAsync(a, w.E2, "8");
        await w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.SchuelerZiel, w.O.Klasse6B, new DateOnly(2026, 11, 2));
        var b = await w.BewertungAsync(w.O.Kf6BMath, titel: "Décembre", datum: new DateOnly(2026, 12, 1));
        var neu = await w.MitDbAsync(db => db.Einschreibungen.Where(e => e.SchuelerId == w.O.SchuelerZiel && e.KlasseId == w.O.Klasse6B).Select(e => e.Id).SingleAsync());
        await w.NoteAsync(b, neu, "12");
        var u6B = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6B, w.O.P1);
        var zeile = Assert.Single(u6B.Zeilen);
        Assert.Equal(10m, zeile.Moyenne.Wert); // (8 + 12) / 2: Note aus 6e A zählt mit
        var u6A = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(1, u6A.Effectif); // am Periodenende nicht mehr in 6e A
    }

    [Fact]
    public async Task Jahresbilanz_in_der_letzten_Periode_mit_Entscheidung()
    {
        var w = await NotenWelt.ErstelleAsync();
        foreach (var p in new[] { w.O.P1, w.O.P2, w.O.P3 })
        {
            var b = await w.BewertungAsync(w.O.Kf6AMath, p, "Composition");
            await w.NoteAsync(b, w.E1, p == w.O.P3 ? "16" : "10");
            await w.NoteAsync(b, w.E2, "12");
        }
        var u = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P3);
        Assert.True(u.IstLetztePeriode);
        await w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, w.E1, new EntscheidungEingabe(JahresEntscheidungsArt.Admis, w.O.Stufe5, null));
        var d = await w.Lesen.EntwurfAsync(w.D, w.O.Klasse6A, w.O.P3, w.E1);
        Assert.Equal("12,00", d.Snapshot.Inhalt.Bilan!.Moyenne);
        Assert.Equal("Admis(e) en classe supérieure", d.Snapshot.Inhalt.Bilan.Entscheidung);
        Assert.Equal(3, d.Snapshot.Inhalt.Bilan.Perioden.Count);
        Assert.Equal("1er ex", d.Snapshot.Inhalt.Bilan.Rang);
        Assert.True(d.Snapshot.Dokument!.Entwurf);
    }
}
