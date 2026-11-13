using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Stufen, Klassen, Fächer, Koeffizienten und Lehraufträge: löschen solange unbenutzt, sonst deaktivieren.</summary>
public class KlassenUndFaecherTests
{
    [Fact]
    public async Task Stufe_Klasse_Fach_loeschen_wenn_unbenutzt_sonst_deaktivieren()
    {
        var w = await OrgWelt.ErstelleAsync();
        var neueStufe = await w.D.Klassen.StufeSpeichernAsync(w.Direktor, null, new StufeEingabe("4e", null, null));
        Assert.Equal(LoeschErgebnis.Geloescht, await w.D.Klassen.StufeLoeschenAsync(w.Direktor, neueStufe));
        Assert.Equal(LoeschErgebnis.Deaktiviert, await w.D.Klassen.StufeLoeschenAsync(w.Direktor, w.Stufe5));
        Assert.False((await w.D.Klassen.StufenAsync(w.Direktor)).Single(s => s.Id == w.Stufe5).Aktiv);
        await w.D.Klassen.StufeAktivierenAsync(w.Direktor, w.Stufe5);
        Assert.True((await w.D.Klassen.StufenAsync(w.Direktor)).Single(s => s.Id == w.Stufe5).Aktiv);

        // 5e A: ohne Einschreibung → gelöscht (samt Klassenfächern); 6e A: mit Schülern → deaktiviert.
        Assert.Equal(LoeschErgebnis.Geloescht, await w.D.Klassen.KlasseLoeschenAsync(w.Direktor, w.Klasse5A));
        Assert.Equal(LoeschErgebnis.Deaktiviert, await w.D.Klassen.KlasseLoeschenAsync(w.Direktor, w.Klasse6A));
        var klassen = await w.D.Klassen.KlassenAsync(w.Direktor, w.Jahr);
        Assert.DoesNotContain(klassen, k => k.Id == w.Klasse5A);
        Assert.False(klassen.Single(k => k.Id == w.Klasse6A).Aktiv);
        Assert.DoesNotContain(await w.D.Klassen.KlassenAuswahlAsync(w.Direktor, w.Jahr), k => k.Id == w.Klasse6A);
        await w.D.Klassen.KlasseAktivierenAsync(w.Direktor, w.Klasse6A);

        var neuesFach = await w.D.Faecher.FachSpeichernAsync(w.Direktor, null, new FachEingabe("Anglais", "ang", FachGruppe.Lettres));
        Assert.Equal("ANG", (await w.D.Faecher.FaecherAsync(w.Direktor)).Single(f => f.Id == neuesFach).Kuerzel);
        Assert.Equal(LoeschErgebnis.Geloescht, await w.D.Faecher.FachLoeschenAsync(w.Direktor, neuesFach));
        Assert.Equal(LoeschErgebnis.Deaktiviert, await w.D.Faecher.FachLoeschenAsync(w.Direktor, w.FachFr));
        // Deaktivierte Fächer können keiner Klasse mehr zugeordnet werden.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.FaecherHinzufuegenAsync(w.Direktor, w.Klasse6B, [w.FachFr]));
    }

    [Fact]
    public async Task Namen_sind_eindeutig_und_Klassen_gehoeren_zum_Jahr()
    {
        var w = await OrgWelt.ErstelleAsync();
        Assert.Equal("name", (await Assert.ThrowsAsync<FachFehler>(() => w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.Jahr, null,
            new KlasseEingabe("6e A", w.Stufe6, null, null, null, null)))).Feld);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Faecher.FachSpeichernAsync(w.Direktor, null, new FachEingabe("mathématiques", null, FachGruppe.Aucune)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Klassen.StufeSpeichernAsync(w.Direktor, null, new StufeEingabe("6E", null, null)));
        // Gleicher Name im nächsten Jahr erlaubt; Session-Klasse nur mit Periode desselben Jahres.
        var naechstes = await w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.JahrVorb, null, new KlasseEingabe("6e A", w.Stufe6, w.PV1, 30, w.Direktor, "Salle 1"));
        Assert.NotEqual(Guid.Empty, naechstes);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.JahrVorb, null, new KlasseEingabe("6e S", null, w.P1, null, null, null)));
        // Professeur principal muss Lehrkraft der Schule sein (Direktor erlaubt, Schüler nicht).
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.Jahr, w.Klasse6B, new KlasseEingabe("6e B", w.Stufe6, null, null, w.Schueler, null)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.Jahr, w.Klasse6B, new KlasseEingabe("6e B", w.Stufe6, null, 0, null, null)));
        await w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.Jahr, w.Klasse6B, new KlasseEingabe("6e B", w.Stufe6, null, 35, w.Lehrer2, "Salle 4"));
        var b = (await w.D.Klassen.KlassenAsync(w.Direktor, w.Jahr)).Single(k => k.Id == w.Klasse6B);
        Assert.Equal(35, b.Kapazitaet);
        Assert.Equal(w.Lehrer2, b.KlassenlehrerId);
        Assert.Equal(1, b.AnzahlFaecher);
        Assert.Equal(0, b.FaecherOhneLehrer);
        Assert.Equal(2, (await w.D.Klassen.KlassenAsync(w.Direktor, w.Jahr)).Single(k => k.Id == w.Klasse6A).Effectif);
    }

    [Fact]
    public async Task Koeffizienten_kopieren_auf_Klasse_und_Stufe()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.KoeffizientSetzenAsync(w.Direktor, w.Kf6AFr, 0));
        await w.D.Unterricht.KoeffizientSetzenAsync(w.Direktor, w.Kf6AFr, 2.555m);
        Assert.Equal(2.56m, (await w.D.Unterricht.KlassenfaecherAsync(w.Direktor, w.Klasse6A)).Single(f => f.Id == w.Kf6AFr).Koeffizient);

        Assert.Equal(2, await w.D.Unterricht.KoeffizientenKopierenAsync(w.Direktor, w.Klasse6A, w.Klasse6B));
        var b = await w.D.Unterricht.KlassenfaecherAsync(w.Direktor, w.Klasse6B);
        Assert.Equal(3m, b.Single(f => f.FachId == w.FachMath).Koeffizient);
        Assert.Equal(2.56m, b.Single(f => f.FachId == w.FachFr).Koeffizient);
        // Lehraufträge der Zielklasse bleiben erhalten.
        Assert.Single(b.Single(f => f.FachId == w.FachMath).Lehrer);

        var c = await w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.Jahr, null, new KlasseEingabe("6e C", w.Stufe6, null, null, null, null));
        Assert.Equal(2, await w.D.Unterricht.KoeffizientenAufStufeAsync(w.Direktor, w.Klasse6A));
        Assert.Equal(2, (await w.D.Unterricht.KlassenfaecherAsync(w.Direktor, c)).Count);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.KoeffizientenKopierenAsync(w.Direktor, w.Klasse6A, w.Klasse6A));
    }

    [Fact]
    public async Task Lehrauftrag_fuer_Lehrkraefte_inklusive_Direktion_mit_Stunden()
    {
        var w = await OrgWelt.ErstelleAsync();
        var auftrag = await w.D.Unterricht.LehrauftragSetzenAsync(w.Koordinator, w.Kf6AFr, w.Direktor, 2.2m);
        var zeile = (await w.D.Unterricht.AuftraegeDesLehrersAsync(w.Koordinator, w.Direktor, w.Jahr)).Single();
        Assert.Equal(auftrag, zeile.Id);
        Assert.Equal(2m, zeile.Wochenstunden);
        // Upsert: gleicher Lehrer, neues Stundenmaß.
        Assert.Equal(auftrag, await w.D.Unterricht.LehrauftragSetzenAsync(w.Koordinator, w.Kf6AFr, w.Direktor, 4));
        Assert.Equal(4m, (await w.D.Unterricht.AuftraegeDesLehrersAsync(w.Koordinator, w.Direktor, w.Jahr)).Single().Wochenstunden);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.LehrauftragSetzenAsync(w.Direktor, w.Kf6AFr, w.Schueler, 2));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.LehrauftragSetzenAsync(w.Direktor, w.Kf6AFr, w.Lehrer2, 0));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.LehrauftragSetzenAsync(w.Direktor, w.Kf6AFr, w.Lehrer2, 41));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Unterricht.LehrauftragSetzenAsync(w.Direktor, w.Kf6AFr, w.DirektorB, 2));

        // Im Stundenplan benutzt → weder Lehrauftrag noch Klassenfach entfernbar.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.LehrauftragEntfernenAsync(w.Direktor, w.Auftrag));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Unterricht.KlassenfachEntfernenAsync(w.Direktor, w.Kf6AMath));
        await w.D.Unterricht.KlassenfachEntfernenAsync(w.Direktor, w.Kf6AFr);
        Assert.Empty(await w.D.Unterricht.AuftraegeDesLehrersAsync(w.Direktor, w.Direktor, w.Jahr));
        Assert.Contains(await w.D.Unterricht.KlassenfaecherDesJahresAsync(w.Direktor, w.Jahr), x => x.Name == "6e A · Mathématiques");
        Assert.False(await w.MitDbAsync(db => db.Klassenfaecher.AnyAsync(k => k.Id == w.Kf6AFr)));
    }

    [Fact]
    public async Task Fachreihenfolge_verschieben()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Faecher.VerschiebenAsync(w.Direktor, w.FachFr, -1);
        Assert.Equal(new[] { w.FachFr, w.FachMath }, (await w.D.Faecher.FaecherAsync(w.Direktor)).Select(f => f.Id).ToArray());
        await w.D.Faecher.VerschiebenAsync(w.Direktor, w.FachFr, -1);
        Assert.Equal(new[] { w.FachFr, w.FachMath }, (await w.D.Faecher.FaecherAsync(w.Direktor)).Select(f => f.Id).ToArray());
        await w.D.Klassen.StufenReihenfolgeAsync(w.Direktor, [w.Stufe5, w.Stufe6]);
        Assert.Equal(new[] { "5e", "6e" }, (await w.D.Klassen.StufenAsync(w.Direktor)).Select(s => s.Name).ToArray());
    }
}
