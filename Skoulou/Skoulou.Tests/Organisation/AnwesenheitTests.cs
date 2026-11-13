using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Klassenappell: Standard « présent », Datum im Jahr/in offener Periode/nicht in der Zukunft, Normalisierung, Übersichten.</summary>
public class AnwesenheitTests
{
    [Fact]
    public async Task Appell_Standard_praesent_speichern_und_ueberschreiben()
    {
        var w = await OrgWelt.ErstelleAsync();
        var appell = await w.D.Anwesenheit.AppellAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute);
        Assert.Null(appell.Sperre);
        Assert.False(appell.Erfasst);
        Assert.Equal("1er trimestre", appell.Periode);
        Assert.Equal(2, appell.Zeilen.Count);
        Assert.All(appell.Zeilen, z => Assert.Equal(AnwesenheitsStatus.Present, z.Status));

        var anzahl = await w.D.Anwesenheit.AppellSpeichernAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute,
        [
            new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Absent, 30, true, "Malade"),
            new AppellEingabe(w.EinschreibungZiel, AnwesenheitsStatus.Present, 10, true, null),
        ]);
        Assert.Equal(2, anzahl);
        appell = await w.D.Anwesenheit.AppellAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute);
        Assert.True(appell.Erfasst);
        Assert.Equal("Test prof", appell.ErfasstVon);
        var abwesend = appell.Zeilen.Single(z => z.EinschreibungId == w.EinschreibungSchueler);
        Assert.Equal(AnwesenheitsStatus.Absent, abwesend.Status);
        Assert.Null(abwesend.Minuten);
        Assert.True(abwesend.Entschuldigt);
        var praesent = appell.Zeilen.Single(z => z.EinschreibungId == w.EinschreibungZiel);
        Assert.False(praesent.Entschuldigt);
        Assert.Null(praesent.Minuten);

        // Gleicher Tag überschreibt (ein Eintrag je Schüler und Tag); Verspätung braucht Minuten.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Retard, null, false, null)]));
        await w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Retard, 15, false, null)]);
        Assert.Equal(2, await w.MitDbAsync(db => db.Anwesenheiten.CountAsync()));
        var eintrag = await w.MitDbAsync(db => db.Anwesenheiten.SingleAsync(a => a.EinschreibungId == w.EinschreibungSchueler));
        Assert.Equal(AnwesenheitsStatus.Retard, eintrag.Status);
        Assert.Equal(15, eintrag.Minuten);
        Assert.Equal(w.Direktor, eintrag.ErfasstVonId);
        // Fremde Einschreibung in der Liste → neu laden.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(Guid.NewGuid(), AnwesenheitsStatus.Absent, null, false, null)]));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Retard, 1441, false, null)]));
    }

    [Theory]
    [InlineData(2026, 11, 14, "future")]
    [InlineData(2026, 8, 31, "en dehors")]
    [InlineData(2026, 9, 3, "aucune période")]
    public async Task Kein_Appell_an_gesperrten_Tagen(int j, int m, int t, string meldung)
    {
        var w = await OrgWelt.ErstelleAsync();
        // Unterrichtsbeginn erst am 7. September: die ersten Tage des Jahres gehören zu keiner Periode.
        await w.MitDbAsync(async db =>
        {
            db.Perioden.Single(p => p.Id == w.P1).Beginn = new DateOnly(2026, 9, 7);
            return await db.SaveChangesAsync();
        });
        var tag = new DateOnly(j, m, t);
        var appell = await w.D.Anwesenheit.AppellAsync(w.Direktor, w.Klasse6A, tag);
        Assert.Contains(meldung, appell.Sperre);
        Assert.Contains(meldung, (await Assert.ThrowsAsync<FachFehler>(() => w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, tag, []))).Message);
    }

    [Fact]
    public async Task Geschlossene_Periode_und_abgeschlossenes_Jahr_sperren_den_Appell()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Jahre.PeriodeStatusAsync(w.Direktor, w.P1, true);
        Assert.Contains("fermée", (await Assert.ThrowsAsync<FachFehler>(() => w.D.Anwesenheit.AppellSpeichernAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute, []))).Message);
        await w.D.Jahre.PeriodeStatusAsync(w.Direktor, w.P1, false);
        await w.D.Anwesenheit.AppellSpeichernAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute, []);
        await w.D.Jahre.JahrAbschliessenAsync(w.Direktor, w.Jahr);
        Assert.Contains("clôturée", (await Assert.ThrowsAsync<FachFehler>(() => w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute, []))).Message);
    }

    [Fact]
    public async Task Veroeffentlichte_Bulletins_sperren_die_Periode()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.MitDbAsync(async db =>
        {
            db.KlassenVeroeffentlichungen.Add(new KlassenVeroeffentlichung { SchuleId = w.SchuleA, KlasseId = w.Klasse6A, PeriodeId = w.P1, Status = VeroeffentlichungsStatus.Publiee, VeroeffentlichtVonId = w.Direktor });
            return await db.SaveChangesAsync();
        });
        Assert.Contains("publiés", (await w.D.Anwesenheit.AppellAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute)).Sperre);
    }

    [Fact]
    public async Task Uebersicht_je_Klasse_und_je_Schueler()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, new(2026, 11, 12),
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Absent, null, false, null)]);
        await w.D.Anwesenheit.AppellSpeichernAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Absent, null, true, "Certificat"),
             new AppellEingabe(w.EinschreibungZiel, AnwesenheitsStatus.Retard, 20, false, null)]);

        var klasse = await w.D.Anwesenheit.KlassenUebersichtAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute, new(2026, 9, 1));
        Assert.Equal(2, klasse.TageMitAppell);
        var schueler = klasse.Zeilen.Single(z => z.SchuelerId == w.Schueler).Summe;
        Assert.Equal(2, schueler.Absences);
        Assert.Equal(1, schueler.AbsencesJustifiees);
        Assert.Equal(1, schueler.NonJustifiees);
        var ziel = klasse.Zeilen.Single(z => z.SchuelerId == w.SchuelerZiel).Summe;
        Assert.Equal(1, ziel.Retards);
        Assert.Equal(20, ziel.RetardMinuten);

        var eigene = await w.D.Anwesenheit.SchuelerAsync(w.Eltern, w.Schueler);
        Assert.Equal("2026-2027", eigene.Jahr);
        Assert.Equal(2, eigene.Eintraege.Count);
        Assert.Equal(OrgWelt.Heute, eigene.Eintraege[0].Datum);
        Assert.Equal("6e A", eigene.Eintraege[0].Klasse);
        Assert.Equal(2, (await w.D.Anwesenheit.SchuelerAsync(w.Schueler, w.Schueler)).Summe.Absences);

        // Nach einem Klassenwechsel bleibt die alte Anwesenheit beim Schüler sichtbar.
        await w.D.Einschreibung.KlasseWechselnAsync(w.Direktor, w.Schueler, w.Klasse6B, OrgWelt.Heute.AddDays(1));
        Assert.Equal(2, (await w.D.Anwesenheit.SchuelerAsync(w.Direktor, w.Schueler)).Eintraege.Count);
    }
}
