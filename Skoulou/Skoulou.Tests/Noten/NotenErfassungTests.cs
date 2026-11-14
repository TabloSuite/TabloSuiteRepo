using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Noten;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Notenraster, Bewertungen, Komponenten: Rechte (Lehrauftrag, Professeur principal, Direktion), Historie, Sperrmatrix.</summary>
public class NotenErfassungTests
{
    [Fact]
    public async Task Lehrer_mit_Lehrauftrag_erfasst_Noten_mit_Historie_und_Audit()
    {
        var w = await NotenWelt.ErstelleAsync();
        var b = await w.BewertungAsync(w.O.Kf6AMath);
        var stand = await w.NoteAsync(b, w.E1, "12,5", w.O.Lehrer);
        Assert.True(stand.Geaendert);
        Assert.Equal(12.5m, stand.Mittel.Wert);
        Assert.False((await w.NoteAsync(b, w.E1, " 12.5 ", w.O.Lehrer)).Geaendert); // identisch = nichts
        w.Zeit.Vor(TimeSpan.FromMinutes(5));
        await w.NoteAsync(b, w.E1, "14", w.O.Lehrer);

        var historie = await w.Noten.HistorieAsync(w.O.Lehrer, b, w.E1);
        Assert.Equal(2, historie.Count);
        Assert.Equal(("12,5", "14"), (historie[0].Alt, historie[0].Neu));
        Assert.Equal("—", historie[1].Alt);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.NoteGeaendert)));
        var note = await w.MitDbAsync(db => db.Noten.SingleAsync());
        Assert.Equal(w.O.Lehrer, note.ErfasstVonId);
    }

    [Fact]
    public async Task Ohne_Lehrauftrag_kein_Schreiben_Professeur_principal_liest()
    {
        var w = await NotenWelt.ErstelleAsync();
        var fr = await w.BewertungAsync(w.O.Kf6AFr);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.NoteAsync(fr, w.E1, "12", w.O.Lehrer));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Noten.RasterAsync(w.O.Lehrer2, w.O.Kf6AMath, w.O.P1));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Noten.RasterAsync(w.O.LehrerZiel, w.O.Kf6AMath, w.O.P1));

        await w.AendernAsync(async db => (await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse6A)).KlassenlehrerId = w.O.Lehrer2);
        var raster = await w.Noten.RasterAsync(w.O.Lehrer2, w.O.Kf6AFr, w.O.P1);
        Assert.False(raster.Schreibbar);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.NoteAsync(fr, w.E1, "12", w.O.Lehrer2));
        var auswahl = await w.Noten.AuswahlAsync(w.O.Lehrer2, w.O.Jahr);
        Assert.All(Assert.Single(auswahl.Klassen).Faecher, f => Assert.False(f.Schreibbar));
    }

    [Theory]
    [InlineData('S')]
    [InlineData('E')]
    [InlineData('X')]
    [InlineData('R')]
    public async Task Schueler_Eltern_fremde_Schule_haben_keinen_Zugriff_aufs_Raster(char rolle)
    {
        var w = await NotenWelt.ErstelleAsync();
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Noten.RasterAsync(w.O.Akteur(rolle), w.O.Kf6AMath, w.O.P1));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Noten.AuswahlAsync(w.O.Akteur(rolle), w.O.Jahr));
    }

    [Fact]
    public async Task Direktion_und_Koordination_sehen_alle_Faecher()
    {
        var w = await NotenWelt.ErstelleAsync();
        foreach (var a in new[] { w.O.Direktor, w.O.Koordinator })
        {
            var auswahl = await w.Noten.AuswahlAsync(a, w.O.Jahr);
            Assert.Equal(3, auswahl.Klassen.Count);
            Assert.Contains(auswahl.Perioden, p => p.Aktuell && p.Id == w.O.P1);
        }
        var lehrer = await w.Noten.AuswahlAsync(w.O.Lehrer, w.O.Jahr);
        var klasse = Assert.Single(lehrer.Klassen);
        Assert.Equal("Mathématiques", Assert.Single(klasse.Faecher).Fach);
    }

    [Fact]
    public async Task Status_Kuerzel_Grenzen_und_Entfernen()
    {
        var w = await NotenWelt.ErstelleAsync();
        var b1 = await w.BewertungAsync(w.O.Kf6AMath, titel: "Devoir 1");
        var b2 = await w.BewertungAsync(w.O.Kf6AMath, titel: "Devoir 2", bareme: 10);
        await Assert.ThrowsAsync<FachFehler>(() => w.NoteAsync(b2, w.E1, "11"));
        await w.NoteAsync(b1, w.E1, "16");
        var abs = await w.NoteAsync(b2, w.E1, "abs");
        Assert.Equal(NotenStatus.Absent, abs.Zelle.Status);
        Assert.Equal(8m, abs.Mittel.Wert); // Schule: absence non justifiée = 0 (Standard)
        await w.AendernAsync(async db => (await db.Schulen.SingleAsync(s => s.Id == w.O.SchuleA)).AbsenzOhneEntschuldigungZaehltNull = false);
        Assert.Equal(16m, (await w.NoteAsync(b2, w.E1, "aj")).Mittel.Wert);
        var leer = await w.NoteAsync(b2, w.E1, "");
        Assert.Equal(NotenStatus.Leer, leer.Zelle.Status);
        Assert.Equal(MittelStatus.Incomplet, leer.Mittel.Status);
        var raster = await w.Noten.RasterAsync(w.D, w.O.Kf6AMath, w.O.P1);
        Assert.Equal(4, raster.Erwartet);
        Assert.Equal(1, raster.Erfasst);
        await Assert.ThrowsAsync<FachFehler>(() => w.NoteAsync(b1, Guid.NewGuid(), "12"));
    }

    [Fact]
    public async Task Sperrmatrix_fuer_Noten()
    {
        var w = await NotenWelt.ErstelleAsync();
        var (math, _) = await w.VollstaendigAsync();
        await w.AendernAsync(async db => (await db.Perioden.SingleAsync(p => p.Id == w.O.P1)).Status = PeriodenStatus.Geschlossen);
        Assert.Contains("fermée", (await Assert.ThrowsAsync<FachFehler>(() => w.NoteAsync(math, w.E1, "12"))).Message);
        Assert.NotNull((await w.Noten.RasterAsync(w.D, w.O.Kf6AMath, w.O.P1)).Sperre);
        await w.AendernAsync(async db => (await db.Perioden.SingleAsync(p => p.Id == w.O.P1)).Status = PeriodenStatus.Offen);

        await w.VeroeffentlichenAsync();
        Assert.Contains("publiés", (await Assert.ThrowsAsync<FachFehler>(() => w.NoteAsync(math, w.E1, "12"))).Message);
        await Assert.ThrowsAsync<FachFehler>(() => w.Noten.BemerkungSpeichernAsync(w.D, w.O.Kf6AMath, w.O.P1, w.E1, "Bien"));
        await Assert.ThrowsAsync<FachFehler>(() => w.BewertungAsync(w.O.Kf6AMath));

        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Erreur de saisie");
        await w.NoteAsync(math, w.E1, "16");
        await Assert.ThrowsAsync<FachFehler>(() => w.BewertungAsync(w.O.Kf6AMath)); // Struktur bleibt gesperrt

        await w.AendernAsync(async db => (await db.Schuljahre.SingleAsync(j => j.Id == w.O.Jahr)).Status = SchuljahrStatus.Abgeschlossen);
        Assert.Contains("clôturée", (await Assert.ThrowsAsync<FachFehler>(() => w.NoteAsync(math, w.E1, "15"))).Message);
    }

    [Fact]
    public async Task Bewertungen_aendern_loeschen_und_aus_Komponenten()
    {
        var w = await NotenWelt.ErstelleAsync();
        var b = await w.BewertungAsync(w.O.Kf6AMath);
        await Assert.ThrowsAsync<FachFehler>(() => w.BewertungAsync(w.O.Kf6AMath, datum: new DateOnly(2027, 1, 10)));
        await Assert.ThrowsAsync<FachFehler>(() => w.BewertungAsync(w.O.Kf6AMath, bareme: 0));
        await Assert.ThrowsAsync<FachFehler>(() => w.BewertungAsync(w.O.Kf6AMath, gewicht: 1.005m));
        await w.NoteAsync(b, w.E1, "12");
        await w.Bewertungen.AendernAsync(w.D, b, new BewertungEingabe("Devoir surveillé", BewertungsArt.Devoir, new DateOnly(2026, 10, 5), 20, 1));
        await Assert.ThrowsAsync<FachFehler>(() => w.Bewertungen.AendernAsync(w.D, b, new BewertungEingabe("Devoir", BewertungsArt.Devoir, null, 10, 1)));
        await Assert.ThrowsAsync<FachFehler>(() => w.Bewertungen.LoeschenAsync(w.D, b));
        var leer = await w.BewertungAsync(w.O.Kf6AMath, titel: "Sans notes");
        await w.Bewertungen.LoeschenAsync(w.O.Lehrer, leer);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bewertungen.AnlegenAsync(w.O.Lehrer, w.O.Kf6AFr, w.O.P1, new BewertungEingabe("X", BewertungsArt.Oral, null, 20, 1)));

        await Assert.ThrowsAsync<FachFehler>(() => w.Bewertungen.AusKomponentenAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bewertungen.KomponentenVerteilenAsync(w.O.Lehrer, w.O.Jahr, null));
        Assert.Equal(3, await w.Bewertungen.KomponentenVerteilenAsync(w.D, w.O.Jahr, null));
        Assert.Equal(3, await w.Bewertungen.AusKomponentenAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1));
        await Assert.ThrowsAsync<FachFehler>(() => w.Bewertungen.AusKomponentenAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1));
        var raster = await w.Noten.RasterAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1);
        Assert.Equal(4, raster.Spalten.Count);
        Assert.Equal(2m, raster.Spalten.Single(s => s.Titel == "Composition").Gewicht);
    }

    [Fact]
    public async Task Komponenten_verwalten()
    {
        var w = await NotenWelt.ErstelleAsync();
        var id = await w.Bewertungen.KomponenteAnlegenAsync(w.D, w.O.P1, new KomponenteEingabe("Interrogation", BewertungsArt.Interrogation, 1, 20));
        await Assert.ThrowsAsync<FachFehler>(() => w.Bewertungen.KomponenteAnlegenAsync(w.D, w.O.P1, new KomponenteEingabe("interrogation", BewertungsArt.Interrogation, 1, 20)));
        await w.Bewertungen.KomponenteAendernAsync(w.O.Koordinator, id, new KomponenteEingabe("Interro", BewertungsArt.Interrogation, 2, 10));
        Assert.Equal(2, await w.Bewertungen.KomponentenVerteilenAsync(w.D, w.O.Jahr, w.O.P1)); // P2 und P3 bekommen die Komponente
        var b = (await w.Bewertungen.AusKomponentenAsync(w.D, w.O.Kf6AMath, w.O.P1));
        Assert.Equal(1, b);
        await w.Bewertungen.KomponenteLoeschenAsync(w.D, id);
        Assert.Null(await w.MitDbAsync(db => db.Bewertungen.Select(x => x.KomponenteId).SingleAsync()));
        var liste = await w.Bewertungen.KomponentenAsync(w.O.Lehrer, w.O.Jahr);
        Assert.Empty(liste.Single(p => p.PeriodeId == w.O.P1).Komponenten);
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bewertungen.KomponenteAnlegenAsync(w.O.Lehrer, w.O.P1, new KomponenteEingabe("X", BewertungsArt.Oral, 1, 20)));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Bewertungen.KomponenteAnlegenAsync(w.O.DirektorB, w.O.P1, new KomponenteEingabe("X", BewertungsArt.Oral, 1, 20)));
    }

    [Fact]
    public async Task Appreciation_des_Lehrers_speichern_und_entfernen()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.Noten.BemerkungSpeichernAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1, w.E1, "  Bon   travail ");
        var raster = await w.Noten.RasterAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1);
        Assert.Equal("Bon travail", raster.Zeilen.Single(z => z.EinschreibungId == w.E1).Bemerkung);
        await w.Noten.BemerkungSpeichernAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1, w.E1, " ");
        Assert.Equal(0, await w.MitDbAsync(db => db.FachBemerkungen.CountAsync()));
        await Assert.ThrowsAsync<FachFehler>(() => w.Noten.BemerkungSpeichernAsync(w.O.Lehrer, w.O.Kf6AMath, w.O.P1, w.E1, new string('x', 301)));
    }

    [Fact]
    public async Task Raster_zeigt_Klassenwechsel_und_erwartete_Zellen()
    {
        var w = await NotenWelt.ErstelleAsync();
        var vorher = await w.BewertungAsync(w.O.Kf6AMath, titel: "Octobre", datum: new DateOnly(2026, 10, 1));
        var nachher = await w.BewertungAsync(w.O.Kf6AMath, titel: "Novembre", datum: new DateOnly(2026, 11, 20));
        await w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.SchuelerZiel, w.O.Klasse6B, new DateOnly(2026, 11, 2));
        var raster = await w.Noten.RasterAsync(w.D, w.O.Kf6AMath, w.O.P1);
        var zeile = raster.Zeilen.Single(z => z.EinschreibungId == w.E2);
        Assert.Contains("changé de classe", zeile.Hinweis);
        Assert.True(zeile.Zellen.Single(c => c.BewertungId == vorher).Erwartet);
        Assert.False(zeile.Zellen.Single(c => c.BewertungId == nachher).Erwartet);
    }
}
