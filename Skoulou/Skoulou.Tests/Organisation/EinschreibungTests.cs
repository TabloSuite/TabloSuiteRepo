using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Einschreibungen: eine aktive je Schüler, Klassenwechsel schließt die alte, Départ, Stornierung, Sammel-Einschreibung.</summary>
public class EinschreibungTests
{
    private static Task<List<Einschreibung>> EinschreibungenAsync(OrgWelt w, Guid schueler)
        => w.MitDbAsync(db => db.Einschreibungen.Where(e => e.SchuelerId == schueler).OrderBy(e => e.Von).ToListAsync());

    [Fact]
    public async Task Klassenwechsel_schliesst_alte_Einschreibung_am_Vortag()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Einschreibung.WiederholerSetzenAsync(w.Direktor, w.EinschreibungZiel, true);
        var neu = await w.D.Einschreibung.KlasseWechselnAsync(w.Koordinator, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 2));
        var liste = await EinschreibungenAsync(w, w.SchuelerZiel);
        Assert.Equal(2, liste.Count);
        var alt = liste[0];
        Assert.Equal(EinschreibungsStatus.Changee, alt.Status);
        Assert.Equal(new DateOnly(2026, 11, 1), alt.Bis);
        var aktiv = Assert.Single(liste, e => e.Status == EinschreibungsStatus.Active);
        Assert.Equal(neu, aktiv.Id);
        Assert.Equal(w.Klasse6B, aktiv.KlasseId);
        Assert.Equal(new DateOnly(2026, 11, 2), aktiv.Von);
        Assert.Null(aktiv.Bis);
        Assert.True(aktiv.IstWiederholer);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.KlasseGewechselt)));
        Assert.Equal("6e B", (await w.D.Schueler.DetailAsync(w.Direktor, w.SchuelerZiel)).Aktive!.Klasse);

        // Ungültige Wechsel.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 20)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6A, new(2026, 11, 2)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6A, new(2027, 8, 1)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerOhne, w.Klasse6A, new(2026, 11, 2)));
        var naechstesJahr = await w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.JahrVorb, null, new KlasseEingabe("5e A", w.Stufe5, null, null, null, null));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, naechstesJahr, new(2026, 12, 1)));
    }

    [Fact]
    public async Task Stornieren_nach_Wechsel_macht_vorherige_wieder_aktiv()
    {
        var w = await OrgWelt.ErstelleAsync();
        var neu = await w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 2));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.StornierenAsync(w.Direktor, neu, " "));
        await w.D.Einschreibung.StornierenAsync(w.Direktor, neu, "Mauvaise classe choisie");
        var liste = await EinschreibungenAsync(w, w.SchuelerZiel);
        var storniert = liste.Single(e => e.Id == neu);
        Assert.NotNull(storniert.StorniertAmUtc);
        Assert.Equal("Mauvaise classe choisie", storniert.StornoGrund);
        Assert.NotEqual(EinschreibungsStatus.Active, storniert.Status);
        var wieder = liste.Single(e => e.Id == w.EinschreibungZiel);
        Assert.Equal(EinschreibungsStatus.Active, wieder.Status);
        Assert.Null(wieder.Bis);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.StornierenAsync(w.Direktor, neu, "Encore"));

        // Mit Noten keine Stornierung (dann « Changer de classe » oder « Départ »).
        await w.MitDbAsync(async db =>
        {
            db.Noten.Add(new Note { SchuleId = w.SchuleA, BewertungId = Guid.NewGuid(), EinschreibungId = w.EinschreibungSchueler, Punkte = 12, ErfasstVonId = w.Lehrer });
            return await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.StornierenAsync(w.Direktor, w.EinschreibungSchueler, "Erreur"));
    }

    [Fact]
    public async Task Depart_mit_Datum_und_Grund_optional_archiviert()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 8, 31), "Départ", false));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 11, 10), "", false));
        await w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 11, 10), "Déménagement", archivieren: true);
        var e = (await EinschreibungenAsync(w, w.SchuelerZiel)).Single();
        Assert.Equal(EinschreibungsStatus.Partie, e.Status);
        Assert.Equal(new DateOnly(2026, 11, 10), e.Bis);
        Assert.Equal("Déménagement", e.Austrittsgrund);
        Assert.Equal(KontoStatus.Archive, (await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == w.SchuelerZiel))).Status);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 11, 11), "Encore", false));
    }

    [Fact]
    public async Task Einschreiben_einzeln_und_als_Liste()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.Schueler, w.Klasse6B, new(2026, 11, 2), false));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.SchuelerOhne, w.Klasse6B, new(2027, 7, 2), false));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.SchuelerWartend, w.Klasse6B, new(2026, 11, 2), false));
        Assert.Equal(1, (await w.D.Schueler.ZaehlerAsync(w.Direktor)).SansClasse);

        var ergebnis = await w.D.Einschreibung.MehrereEinschreibenAsync(w.Direktor, w.Klasse6B,
            [w.SchuelerOhne, w.Schueler, w.SchuelerWartend], new(2026, 11, 2), wiederholer: false);
        Assert.Equal(1, ergebnis.Eingeschrieben);
        Assert.Equal(2, ergebnis.Uebersprungen.Count);
        Assert.Equal(0, (await w.D.Schueler.ZaehlerAsync(w.Direktor)).SansClasse);
        Assert.Single(await EinschreibungenAsync(w, w.SchuelerOhne));
        var klasse = (await w.D.Klassen.KlassenAsync(w.Direktor, w.Jahr)).Single(k => k.Id == w.Klasse6B);
        Assert.Equal(1, klasse.Effectif);
        var liste = await w.D.Schueler.SucheAsync(w.Direktor, null, w.Klasse6B, null, 0, 50);
        Assert.Equal("6e B", Assert.Single(liste.Eintraege).Klasse);
        // Deaktivierte Klasse nimmt niemanden auf.
        await w.D.Klassen.KlasseLoeschenAsync(w.Direktor, w.Klasse6A);
        await w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 11, 12), "Départ", false);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.SchuelerZiel, w.Klasse6A, OrgWelt.Heute, false));
    }

    [Fact]
    public void Vorgabe_ist_Schuljahresbeginn_bzw_Tag_nach_frueherer_Einschreibung()
    {
        DateOnly beginn = new(2026, 9, 1), ende = new(2027, 6, 30);
        Assert.Equal(beginn, EinschreibungsBeginn.Vorgabe(beginn, ende));
        Assert.Equal(beginn, EinschreibungsBeginn.Vorgabe(beginn, ende, [null, new DateOnly(2026, 8, 31)]));
        Assert.Equal(new DateOnly(2026, 11, 11), EinschreibungsBeginn.Vorgabe(beginn, ende, [new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 10)]));
        Assert.Equal(ende, EinschreibungsBeginn.Vorgabe(beginn, ende, [ende]));
        // Höchstens heute im laufenden Jahr, sonst Jahresende.
        Assert.Equal(OrgWelt.Heute, EinschreibungsBeginn.Hoechstens(beginn, ende, OrgWelt.Heute));
        Assert.Equal(ende, EinschreibungsBeginn.Hoechstens(beginn, ende, new(2026, 8, 20)));
        Assert.Equal(ende, EinschreibungsBeginn.Hoechstens(beginn, ende, new(2027, 7, 14)));
    }

    [Fact]
    public async Task Schulstart_mitten_im_Jahr_Einschreibung_ab_Schuljahresbeginn_Datum_aenderbar()
    {
        // Klicktest 20.11.2026: Anlage, Validierung und Import schreiben standardmäßig ab Beginn des Schuljahres ein.
        var w = await OrgWelt.ErstelleAsync();
        var standard = await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Afi", "Koffi"), w.Klasse6A);
        Assert.Equal(new DateOnly(2026, 9, 1), (await w.D.Schueler.DetailAsync(w.Direktor, standard.Zugaenge[0].KontoId)).Aktive!.Von);
        var neuzugang = await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Yawa", "Tchalla"), w.Klasse6A, von: new DateOnly(2026, 11, 2));
        Assert.Equal(new DateOnly(2026, 11, 2), (await w.D.Schueler.DetailAsync(w.Direktor, neuzugang.Zugaenge[0].KontoId)).Aktive!.Von);
        Assert.Equal("datum", (await Assert.ThrowsAsync<FachFehler>(() =>
            w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Edem", "Kodjo"), w.Klasse6A, von: new DateOnly(2026, 8, 31)))).Feld);

        await w.D.Schueler.ValidierenAsync(w.Direktor, w.SchuelerWartend, w.Klasse6B, false);
        Assert.Equal(new DateOnly(2026, 9, 1), (await w.D.Schueler.DetailAsync(w.Direktor, w.SchuelerWartend)).Aktive!.Von);

        // « Inscrire dans une classe »: Vorgabe Beginn des Schuljahres; nach einem Départ frühestens am Tag danach.
        Assert.Equal(new DateOnly(2026, 9, 1), await w.D.Einschreibung.VorgabeBeginnAsync(w.Direktor, w.Jahr, [w.SchuelerOhne]));
        await w.D.Einschreibung.AustrittAsync(w.Direktor, w.SchuelerZiel, new(2026, 10, 9), "Départ provisoire", false);
        Assert.Equal(new DateOnly(2026, 10, 10), await w.D.Einschreibung.VorgabeBeginnAsync(w.Direktor, w.Jahr, [w.SchuelerOhne, w.SchuelerZiel]));
        var ueberlappt = await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 9, 1), false));
        Assert.Contains("09/10/2026", ueberlappt.Message);
        var sammel = await w.D.Einschreibung.MehrereEinschreibenAsync(w.Direktor, w.Klasse6B, [w.SchuelerOhne, w.SchuelerZiel], new(2026, 9, 1), false);
        Assert.Equal(1, sammel.Eingeschrieben);
        Assert.Contains(sammel.Uebersprungen, u => u.Contains("jusqu'au 09/10/2026"));
        await w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 10, 12), false);
    }

    [Fact]
    public async Task Beginn_korrigieren_nur_im_offenen_Jahr_ohne_Ueberlappung_und_ohne_Noten_davor()
    {
        var w = await OrgWelt.ErstelleAsync();
        var neu = await w.D.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Afi", "Koffi"), w.Klasse6A, von: new DateOnly(2026, 11, 2));
        var e = (await w.D.Schueler.DetailAsync(w.Direktor, neu.Zugaenge[0].KontoId)).Aktive!;
        Assert.True(e.JahrOffen);
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30)), (e.JahrBeginn, e.JahrEnde));

        // Früher setzen (Schüler war seit der Rentrée da) – mit Protokoll; Lehrer/Schüler dürfen nicht.
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Einschreibung.BeginnKorrigierenAsync(w.Lehrer, e.Id, new(2026, 9, 1)));
        await w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, e.Id, new(2026, 9, 1));
        Assert.Equal(new DateOnly(2026, 9, 1), (await w.D.Schueler.DetailAsync(w.Direktor, neu.Zugaenge[0].KontoId)).Aktive!.Von);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.EinschreibungsBeginnKorrigiert)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, e.Id, new(2026, 8, 31)));

        // Nutzer 21.11.2026 (« 2 gut »): wie alle Einschreibungsaktionen darf auch der Koordinator korrigieren
        // (KontenVerwalten hat in der Rechte-Matrix jeder Koordinator, kein Zusatzrecht); Schüler und Eltern nie.
        await w.D.Einschreibung.BeginnKorrigierenAsync(w.Koordinator, e.Id, new(2026, 9, 2));
        await w.D.Einschreibung.BeginnKorrigierenAsync(w.Koordinator, e.Id, new(2026, 9, 1));
        Assert.Equal(3, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.EinschreibungsBeginnKorrigiert)));
        await Assert.ThrowsAnyAsync<Exception>(() => w.D.Einschreibung.BeginnKorrigierenAsync(w.SchuelerZiel, e.Id, new(2026, 9, 2)));

        // Später setzen: nicht hinter Noten, die schon vor dem neuen Datum liegen.
        await w.MitDbAsync(async db =>
        {
            var b = new Bewertung { SchuleId = w.SchuleA, KlassenfachId = w.Kf6AMath, PeriodeId = w.P1, Titel = "Devoir 1", Datum = new(2026, 10, 5), ErstelltVonId = w.Lehrer };
            db.Bewertungen.Add(b);
            db.Noten.Add(new Note { SchuleId = w.SchuleA, BewertungId = b.Id, EinschreibungId = e.Id, Punkte = 12, Status = NotenStatus.Notee, ErfasstVonId = w.Lehrer });
            return await db.SaveChangesAsync();
        });
        Assert.Equal("datum", (await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, e.Id, new(2026, 11, 2)))).Feld);
        await w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, e.Id, new(2026, 10, 1));

        // Nach einem Klassenwechsel nicht vor das Ende der vorherigen Einschreibung.
        var gewechselt = await w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 2));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, gewechselt, new(2026, 10, 15)));
        await w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, gewechselt, new(2026, 11, 5));

        // Nach dem Abschluss des Schuljahres nicht mehr.
        await w.MitDbAsync(async db =>
        {
            (await db.Schuljahre.SingleAsync(j => j.Id == w.Jahr)).Status = SchuljahrStatus.Abgeschlossen;
            return await db.SaveChangesAsync();
        });
        Assert.False((await w.D.Schueler.DetailAsync(w.Direktor, neu.Zugaenge[0].KontoId)).Aktive!.JahrOffen);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.BeginnKorrigierenAsync(w.Direktor, e.Id, new(2026, 9, 1)));
    }
}
