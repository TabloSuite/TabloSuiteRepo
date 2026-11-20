using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Noten;

/// <summary>
/// « Matières facultatives » (19.11.2026): Schalter und Regel je Klassenfach, Wahl je Schüler durch die Direktion, Abweisung
/// fremder Klassen/Schüler/Pflichtfächer, Übertragung beim Klassenwechsel, Sperre im abgeschlossenen Jahr.
/// Testwelt: 6e A mit Mathématiques (coef 3, Pflicht) und Français (coef 2, hier als Wahlfach), eleve + eleveziel.
/// </summary>
public class WahlfachTests
{
    private static async Task<NotenWelt> MitWahlfachAsync(WahlfachRegel regel = WahlfachRegel.PunkteUeber10)
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.O.D.Unterricht.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, regel);
        return w;
    }

    [Fact]
    public async Task Direktion_macht_ein_Fach_facultative_und_waehlt_je_Schueler()
    {
        var w = await MitWahlfachAsync();
        var u = w.O.D.Unterricht;
        var fr = (await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AFr);
        Assert.Equal((true, WahlfachRegel.PunkteUeber10, 0), (fr.IstWahlfach, fr.WahlfachRegel, fr.AnzahlGewaehlt));
        Assert.False((await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AMath).IstWahlfach);
        Assert.Equal(1, (await w.O.D.Klassen.KlassenAsync(w.D, w.O.Jahr)).Single(k => k.Id == w.O.Klasse6A).AnzahlWahlfaecher);

        var ansicht = await u.FachwahlenAsync(w.D, w.O.Klasse6A);
        Assert.True(ansicht.Offen);
        Assert.Equal(w.O.Kf6AFr, Assert.Single(ansicht.Faecher).KlassenfachId);
        Assert.Equal(2, ansicht.Schueler.Count);
        Assert.All(ansicht.Schueler, s => Assert.Empty(s.Gewaehlt));

        Assert.Equal(1, await u.FachwahlenSpeichernAsync(w.O.Koordinator, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]));
        Assert.Equal(0, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler), (w.O.Kf6AFr, w.O.Schueler)]));
        Assert.Equal(w.O.Kf6AFr, Assert.Single((await u.FachwahlenAsync(w.D, w.O.Klasse6A)).Schueler.Single(s => s.SchuelerId == w.O.Schueler).Gewaehlt));
        Assert.Equal(1, (await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AFr).AnzahlGewaehlt);

        // Vollständiger Stand: eleve abgewählt, eleveziel gewählt = zwei Änderungen
        Assert.Equal(2, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.SchuelerZiel)]));
        var wahl = await w.MitDbAsync(db => db.Fachwahlen.SingleAsync());
        Assert.Equal((w.O.SchuelerZiel, w.D), (wahl.SchuelerId, wahl.ErfasstVonId));

        // Regel ändern und ausschalten: gespeicherte Wahlen bleiben (versehentliches Ausschalten verliert nichts)
        await u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, WahlfachRegel.NurInformation);
        Assert.Equal(WahlfachRegel.NurInformation, Assert.Single((await u.FachwahlenAsync(w.D, w.O.Klasse6A)).Faecher).Regel);
        await u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, false, WahlfachRegel.NurInformation);
        Assert.Empty((await u.FachwahlenAsync(w.D, w.O.Klasse6A)).Faecher);
        Assert.Equal(1, await w.MitDbAsync(db => db.Fachwahlen.CountAsync()));
        Assert.Equal("regel", (await Assert.ThrowsAsync<FachFehler>(() => u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, (WahlfachRegel)9))).Feld);
    }

    [Fact]
    public async Task Fremde_Klasse_fremder_Schueler_und_Pflichtfach_werden_abgewiesen()
    {
        var w = await MitWahlfachAsync();
        var u = w.O.D.Unterricht;
        await Assert.ThrowsAsync<FachFehler>(() => u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AMath, w.O.Schueler)]));
        await Assert.ThrowsAsync<FachFehler>(() => u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6BMath, w.O.Schueler)]));
        await Assert.ThrowsAsync<FachFehler>(() => u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.SchuelerOhne)]));
        await Assert.ThrowsAsync<FachFehler>(() => u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Lehrer)]));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.FachwahlenSpeichernAsync(w.O.DirektorB, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.FachwahlenSpeichernAsync(w.O.Lehrer, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.FachwahlenAsync(w.O.DirektorB, w.O.Klasse6A));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => u.WahlfachSetzenAsync(w.O.DirektorB, w.O.Kf6AFr, false, WahlfachRegel.PunkteUeber10));
        Assert.Equal(0, await w.MitDbAsync(db => db.Fachwahlen.CountAsync()));
    }

    [Fact]
    public async Task Klassenwechsel_uebertraegt_die_Wahl_auf_dasselbe_Wahlfach()
    {
        var w = await MitWahlfachAsync();
        var u = w.O.D.Unterricht;
        await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler), (w.O.Kf6AFr, w.O.SchuelerZiel)]);
        await u.FaecherHinzufuegenAsync(w.D, w.O.Klasse6B, [w.O.FachFr], 2);
        var kf6BFr = (await u.KlassenfaecherAsync(w.D, w.O.Klasse6B)).Single(f => f.FachId == w.O.FachFr).Id;

        // 6e B: Français zunächst Pflichtfach → keine Übertragung
        await w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.SchuelerZiel, w.O.Klasse6B, new(2026, 11, 2));
        Assert.False(await w.MitDbAsync(db => db.Fachwahlen.AnyAsync(x => x.KlassenfachId == kf6BFr)));

        // als Wahlfach → die Wahl wandert mit; die Wahl in 6e A bleibt für deren Perioden bestehen
        await u.WahlfachSetzenAsync(w.D, kf6BFr, true, WahlfachRegel.WieNormal);
        await w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.Schueler, w.O.Klasse6B, new(2026, 11, 3));
        var wahlen = await w.MitDbAsync(db => db.Fachwahlen.Where(x => x.SchuelerId == w.O.Schueler).Select(x => x.KlassenfachId).ToListAsync());
        Assert.Equal(2, wahlen.Count);
        Assert.Contains(kf6BFr, wahlen);
        Assert.Contains(w.O.Kf6AFr, wahlen);
        Assert.Equal(kf6BFr, Assert.Single((await u.FachwahlenAsync(w.D, w.O.Klasse6B)).Schueler.Single(s => s.SchuelerId == w.O.Schueler).Gewaehlt));
        // 6e A ohne aktive Schüler: die Wahlen der Ehemaligen bleiben beim Speichern unberührt
        Assert.Empty((await u.FachwahlenAsync(w.D, w.O.Klasse6A)).Schueler);
        Assert.Equal(0, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, []));
        Assert.Equal(3, await w.MitDbAsync(db => db.Fachwahlen.CountAsync()));
    }

    /// <summary>Français als Wahlfach, nur eleve wählt; Mathématiques eleve 15 / eleveziel 11, Français eleve 16.</summary>
    private static async Task<(NotenWelt W, Guid Fr)> BenotetAsync(WahlfachRegel regel = WahlfachRegel.PunkteUeber10)
    {
        var w = await MitWahlfachAsync(regel);
        await w.O.D.Unterricht.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]);
        var math = await w.BewertungAsync(w.O.Kf6AMath, titel: "Interrogation");
        var fr = await w.BewertungAsync(w.O.Kf6AFr, titel: "Dictée");
        await w.NoteAsync(math, w.E1, "15");
        await w.NoteAsync(math, w.E2, "11");
        await w.NoteAsync(fr, w.E1, "16");
        return (w, fr);
    }

    [Fact]
    public async Task Notenerfassung_zeigt_im_Wahlfach_nur_die_Waehlenden()
    {
        var (w, fr) = await BenotetAsync();
        var raster = await w.Noten.RasterAsync(w.D, w.O.Kf6AFr, w.O.P1);
        Assert.Equal(WahlfachRegel.PunkteUeber10, raster.Wahlfach);
        Assert.Equal(w.E1, Assert.Single(raster.Zeilen).EinschreibungId);
        Assert.Equal((1, 1), (raster.Erwartet, raster.Erfasst));
        var math = await w.Noten.RasterAsync(w.D, w.O.Kf6AMath, w.O.P1);
        Assert.Equal((2, (WahlfachRegel?)null), (math.Zeilen.Count, math.Wahlfach));

        Assert.Equal(NotenService.MeldungNichtGewaehlt, (await Assert.ThrowsAsync<FachFehler>(() => w.NoteAsync(fr, w.E2, "12"))).Message);
        await Assert.ThrowsAsync<FachFehler>(() => w.Noten.BemerkungSpeichernAsync(w.D, w.O.Kf6AFr, w.O.P1, w.E2, "Bien"));
        await w.Noten.BemerkungSpeichernAsync(w.D, w.O.Kf6AFr, w.O.P1, w.E1, "Très bien");

        // Cockpit: im Wahlfach nur der Wählende erwartet → 2 (Mathématiques) + 1 (Français) = 3
        var stand = (await w.Lesen.VollstaendigkeitAsync(w.D, w.O.Jahr, w.O.P1)).Single(k => k.KlasseId == w.O.Klasse6A);
        Assert.Equal((3, 3, 100), (stand.Erwartet, stand.Noten, stand.Prozent));
    }

    [Fact]
    public async Task Conseil_und_Bulletin_rechnen_den_Bonus_nur_fuer_den_Waehlenden()
    {
        var (w, _) = await BenotetAsync();
        var u = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Empty(u.Fehlend); // eleveziel wird in Français nicht erwartet
        Assert.Equal(0, u.NonClasses);
        Assert.Equal(19m, u.Zeilen.Single(z => z.EinschreibungId == w.E1).Moyenne.Wert); // (15·3 + (16 − 10)·2) / 3
        Assert.Equal(11m, u.Zeilen.Single(z => z.EinschreibungId == w.E2).Moyenne.Wert);

        await w.VeroeffentlichenAsync();
        var json = await w.MitDbAsync(db => db.Bulletins.ToDictionaryAsync(b => b.EinschreibungId, b => b.SnapshotJson));
        var eleve = BulletinSnapshot.AusJson(json[w.E1]).Inhalt;
        var fr = eleve.Faecher.Single(f => f.Name == "Français");
        Assert.Equal((WahlfachRegel.PunkteUeber10, 12m, "16,00", "1er"), (fr.Wahlfach!.Value, fr.Punkte!.Value, fr.MoyenneText, fr.Rang));
        Assert.Null(eleve.Faecher.Single(f => f.Name == "Mathématiques").Wahlfach);
        Assert.Equal((3m, 57m, "19,00"), (eleve.SummeKoeffizienten, eleve.SummePunkte!.Value, eleve.MoyenneText));
        var bloecke = BulletinPdf.Bloecke(eleve);
        Assert.Equal([null, "MATIÈRES FACULTATIVES"], bloecke.Select(b => b.Kopf));
        Assert.Equal("points au-dessus de 10 ajoutés", bloecke[1].Regel);

        // eleveziel: Français erscheint gar nicht, der Snapshot kennt kein Wahlfach-Feld, kein Bereich « facultatives »
        var ziel = BulletinSnapshot.AusJson(json[w.E2]).Inhalt;
        Assert.Equal(["Mathématiques"], ziel.Faecher.Select(f => f.Name));
        Assert.DoesNotContain("Wahlfach", json[w.E2]);
        Assert.Null(Assert.Single(BulletinPdf.Bloecke(ziel)).Kopf);

        // PDF des Schülers mit Bereich und der ganzen Klasse
        var id = await w.MitDbAsync(db => db.Bulletins.Where(b => b.EinschreibungId == w.E1).Select(b => b.Id).SingleAsync());
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await w.Pdf.BulletinAsync(w.O.Schueler, id, "https://localhost")).Inhalt, 0, 4));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await w.Pdf.KlasseAsync(w.D, w.O.Klasse6A, w.O.P1, "https://localhost")).Inhalt, 0, 4));
    }

    [Theory]
    [InlineData(WahlfachRegel.WieNormal, "15,40", 5, 77)]       // (15·3 + 16·2) / 5
    [InlineData(WahlfachRegel.NurInformation, "15,00", 3, 45)]  // 15·3 / 3, Français nur zur Information
    public async Task Andere_Regeln_im_Bulletin(WahlfachRegel regel, string moyenne, int koeffizienten, int punkte)
    {
        var (w, _) = await BenotetAsync(regel);
        var d = await w.Lesen.EntwurfAsync(w.D, w.O.Klasse6A, w.O.P1, w.E1);
        var i = d.Snapshot.Inhalt;
        Assert.Equal((moyenne, (decimal)koeffizienten, (decimal)punkte), (i.MoyenneText, i.SummeKoeffizienten, i.SummePunkte!.Value));
        Assert.Equal(regel, i.Faecher.Single(f => f.Name == "Français").Wahlfach);
    }

    [Fact]
    public async Task Live_Noten_der_Eltern_nur_mit_gewaehlten_Wahlfaechern()
    {
        var (w, _) = await BenotetAsync();
        await w.AendernAsync(async db => (await db.Schulen.SingleAsync(s => s.Id == w.O.SchuleA)).ElternSehenNotenLive = true);
        var live = await w.Lesen.LiveNotenAsync(w.O.Eltern, w.O.Schueler, w.O.P1);
        Assert.Equal("19,00", live.Moyenne);
        Assert.Equal("Matière facultative · Seuls les points au-dessus de 10 comptent", live.Faecher.Single(f => f.Fach == "Français").Facultative);
        var ziel = await w.Lesen.LiveNotenAsync(w.O.SchuelerZiel, w.O.SchuelerZiel, w.O.P1);
        Assert.Equal(["Mathématiques"], ziel.Faecher.Select(f => f.Fach));
        Assert.Equal("11,00", ziel.Moyenne);
    }

    [Fact]
    public void Tabelle_ohne_Wahlfaecher_und_Gruppen_bleibt_wie_bisher()
    {
        var schule = new Schule { Name = "Collège Test", Ort = "Lomé", Adresse = "BP 1", Telefon = "+228 00", Ministerium = "Ministère", RepublikZeile = "RÉPUBLIQUE TOGOLAISE" };
        var standard = VorlagenService.Beispiel(schule, new BulletinVorlage { SchuleId = schule.Id });
        // Hash des Beispiel-Bulletins vor der Einführung der Wahlfächer (19.11.2026): Inhalt und JSON unverändert.
        Assert.Equal("475b0af7a9223d19bab59d27c1bbb045d6a752e0a1eb0dbedcd2146fd8b0827b", BulletinSnapshot.HashVon(standard));
        Assert.DoesNotContain("Wahlfach", new BulletinSnapshot(1, standard, null).AlsJson());
        var block = Assert.Single(BulletinPdf.Bloecke(standard));
        Assert.Equal((null, 6, null), (block.Kopf, block.Faecher.Count, block.Bilan));

        // Mit Gruppen: Kopf und Bilan je Gruppe; Wahlfächer immer im eigenen Bereich am Ende, auch ohne Gruppen.
        var gruppen = VorlagenService.Beispiel(schule, new BulletinVorlage { SchuleId = schule.Id, ZeigeFachGruppen = true });
        Assert.Equal(["MATIÈRES LITTÉRAIRES", "MATIÈRES SCIENTIFIQUES", "AUTRES MATIÈRES"], BulletinPdf.Bloecke(gruppen).Select(b => b.Kopf));
        Assert.Equal("Lettres", BulletinPdf.Bloecke(gruppen)[0].Bilan!.Name);
        SnapshotFach Wahl(string name, WahlfachRegel r) => new(name, "Lettres", null, 2, 16, "16,00", 12, "1er", null, null, null, r);
        var mitWahl = gruppen with { Faecher = [.. gruppen.Faecher, Wahl("Ewe", WahlfachRegel.PunkteUeber10), Wahl("Culture", WahlfachRegel.NurInformation)] };
        var b = BulletinPdf.Bloecke(mitWahl);
        Assert.Equal(["MATIÈRES LITTÉRAIRES", "MATIÈRES SCIENTIFIQUES", "AUTRES MATIÈRES", "MATIÈRES FACULTATIVES"], b.Select(x => x.Kopf));
        Assert.Equal((null, 2), (b[^1].Regel, b[^1].Faecher.Count)); // verschiedene Regeln: je Fach statt im Kopf
        Assert.Equal(3, b[0].Faecher.Count); // Ewe zählt nicht zu den Lettres
        var ohneGruppen = standard with { Faecher = [.. standard.Faecher, Wahl("Ewe", WahlfachRegel.PunkteUeber10)] };
        Assert.Equal([null, "MATIÈRES FACULTATIVES"], BulletinPdf.Bloecke(ohneGruppen).Select(x => x.Kopf));
        Assert.Contains("\"Wahlfach\":0", new BulletinSnapshot(1, ohneGruppen, null).AlsJson());
        Assert.Equal(WahlfachRegel.PunkteUeber10, BulletinSnapshot.AusJson(new BulletinSnapshot(1, ohneGruppen, null).AlsJson()).Inhalt.Faecher[^1].Wahlfach);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(BulletinPdf.Erzeuge([new BulletinSeite(new BulletinSnapshot(1, mitWahl, null), null, null, BulletinStatus.Publie, null)]), 0, 4));
    }

    [Fact]
    public async Task Nachtraeglich_facultative_Noten_bleiben_und_werden_im_Dialog_gezaehlt()
    {
        // Nutzer 19.11.2026: Noten wurden schon erfasst, dann wird das Fach facultative – nichts geht verloren, der Dialog sagt es.
        var w = await NotenWelt.ErstelleAsync();
        var u = w.O.D.Unterricht;
        var fr = await w.BewertungAsync(w.O.Kf6AFr, titel: "Dictée");
        await w.NoteAsync(fr, w.E1, "16");
        await w.NoteAsync(fr, w.E2, "12");
        async Task<int> OhneWahl() => (await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AFr).NotenOhneWahl;

        Assert.Equal(2, await OhneWahl());                       // Pflichtfach: vor dem Einschalten zählt der Dialog alle Noten
        await u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, WahlfachRegel.PunkteUeber10);
        Assert.Equal(2, await OhneWahl());                       // noch niemand gewählt
        await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]);
        Assert.Equal(1, await OhneWahl());                       // nur noch die Note von eleveziel
        Assert.Equal(2, await w.MitDbAsync(db => db.Noten.CountAsync()));
        Assert.Equal(0, (await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AMath).NotenOhneWahl);
    }

    [Fact]
    public async Task Nach_der_Veroeffentlichung_sind_Regel_und_Wahl_gesperrt_ausser_Rectification_und_Neuankoemmling()
    {
        var (w, _) = await BenotetAsync();
        var u = w.O.D.Unterricht;
        Assert.False((await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AFr).WahlfachGesperrt);
        await w.VeroeffentlichenAsync();

        // Schalter und Regel: Struktur – fest, auch in einer Rectification; unveränderter Stand wird still angenommen.
        Assert.True((await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Single(f => f.Id == w.O.Kf6AFr).WahlfachGesperrt);
        Assert.Equal(UnterrichtService.MeldungWahlfachGesperrt,
            (await Assert.ThrowsAsync<FachFehler>(() => u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, WahlfachRegel.WieNormal))).Message);
        await Assert.ThrowsAsync<FachFehler>(() => u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, false, WahlfachRegel.PunkteUeber10));
        await u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, WahlfachRegel.PunkteUeber10);

        // Wahl der Schüler mit Bulletin: fest; gleicher Stand speichert ohne Fehler.
        var ansicht = await u.FachwahlenAsync(w.D, w.O.Klasse6A);
        Assert.All(ansicht.Schueler, s => Assert.True(s.Gesperrt));
        Assert.Equal(UnterrichtService.MeldungWahlGesperrt, ansicht.Sperrhinweis);
        Assert.Equal(UnterrichtService.MeldungWahlGesperrt, (await Assert.ThrowsAsync<FachFehler>(() =>
            u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler), (w.O.Kf6AFr, w.O.SchuelerZiel)]))).Message);
        await Assert.ThrowsAsync<FachFehler>(() => u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, []));
        Assert.Equal(0, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]));

        // Neu angekommener Schüler ohne Bulletin wählt frei.
        await w.O.D.Einschreibung.EinschreibenAsync(w.D, w.O.SchuelerOhne, w.O.Klasse6A, new(2026, 11, 2), false);
        Assert.False((await u.FachwahlenAsync(w.D, w.O.Klasse6A)).Schueler.Single(s => s.SchuelerId == w.O.SchuelerOhne).Gesperrt);
        Assert.Equal(1, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler), (w.O.Kf6AFr, w.O.SchuelerOhne)]));

        // Während einer Rectification ist die Wahl korrigierbar (wie eine Note), die Regel bleibt fest.
        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Choix oublié");
        var offen = await u.FachwahlenAsync(w.D, w.O.Klasse6A);
        Assert.All(offen.Schueler, s => Assert.False(s.Gesperrt));
        Assert.Null(offen.Sperrhinweis);
        Assert.Equal(1, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A,
            [(w.O.Kf6AFr, w.O.Schueler), (w.O.Kf6AFr, w.O.SchuelerOhne), (w.O.Kf6AFr, w.O.SchuelerZiel)]));
        await Assert.ThrowsAsync<FachFehler>(() => u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, WahlfachRegel.NurInformation));
    }

    /// <summary>
    /// Klicktest 20.11.2026: nach der Veröffentlichung ließ sich der Koeffizient noch ändern (direkt, per Kopie oder « Appliquer au
    /// niveau » – Letzteres überschrieb sogar die gesperrte Wahlfach-Regel). Danach zeigte der Conseil der veröffentlichten Periode
    /// andere Moyennes als die Bulletins der Familien. Jetzt: fest wie die Wahlfach-Regel; unveränderter Wert speichert still.
    /// </summary>
    [Fact]
    public async Task Nach_der_Veroeffentlichung_sind_Koeffizienten_fest_auch_beim_Kopieren()
    {
        var (w, _) = await BenotetAsync();
        var u = w.O.D.Unterricht;
        await w.VeroeffentlichenAsync();
        async Task<Klassenfach> Kf(Guid id) => await w.MitDbAsync(db => db.Klassenfaecher.AsNoTracking().SingleAsync(k => k.Id == id));
        var vorher = await Kf(w.O.Kf6AMath);

        Assert.Equal(UnterrichtService.MeldungKoeffizientGesperrt,
            (await Assert.ThrowsAsync<FachFehler>(() => u.KoeffizientSetzenAsync(w.D, w.O.Kf6AMath, vorher.Koeffizient + 1))).Message);
        await u.KoeffizientSetzenAsync(w.D, w.O.Kf6AMath, vorher.Koeffizient);

        // 6e B (nicht veröffentlicht) als Quelle: Kopie in die 6e A abgewiesen, « Appliquer au niveau » lässt die 6e A aus.
        await u.KoeffizientSetzenAsync(w.D, w.O.Kf6BMath, 7);
        Assert.Equal(UnterrichtService.MeldungKoeffizientGesperrt,
            (await Assert.ThrowsAsync<FachFehler>(() => u.KoeffizientenKopierenAsync(w.D, w.O.Klasse6B, w.O.Klasse6A))).Message);
        Assert.Equal(0, await u.KoeffizientenAufStufeAsync(w.D, w.O.Klasse6B));
        var nachher = await Kf(w.O.Kf6AMath);
        Assert.Equal(vorher.Koeffizient, nachher.Koeffizient);
        Assert.Equal((true, WahlfachRegel.PunkteUeber10), ((await Kf(w.O.Kf6AFr)).IstWahlfach, (await Kf(w.O.Kf6AFr)).WahlfachRegel));
        Assert.True((await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).All(f => f.WahlfachGesperrt));

        // Die 6e A als Quelle für noch offene Klassen bleibt erlaubt.
        Assert.Equal(1, await u.KoeffizientenAufStufeAsync(w.D, w.O.Klasse6A));
        Assert.Equal(vorher.Koeffizient, (await Kf(w.O.Kf6BMath)).Koeffizient);
    }

    [Fact]
    public async Task Jahreswechsel_uebernimmt_die_Wahl_wo_das_Fach_facultative_bleibt()
    {
        var w = await MitWahlfachAsync();
        var u = w.O.D.Unterricht;
        await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler), (w.O.Kf6AFr, w.O.SchuelerZiel)]);
        // 5e A (Ziel der Admis) hat Français als Pflichtfach; die neue 6e A (Ziel der Redoublants) übernimmt das Wahlfach der alten.
        await u.FaecherHinzufuegenAsync(w.D, w.O.Klasse5A, [w.O.FachFr], 2);
        await w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, w.E1, new EntscheidungEingabe(JahresEntscheidungsArt.Admis, null, null));
        await w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, w.E2, new EntscheidungEingabe(JahresEntscheidungsArt.Redouble, null, null));
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        plan.Namen[w.O.Klasse6B] = "6e B bis";
        await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);

        var neu = await w.MitDbAsync(db => (from f in db.Fachwahlen
                                            join kf in db.Klassenfaecher on f.KlassenfachId equals kf.Id
                                            join k in db.Klassen on kf.KlasseId equals k.Id
                                            where k.SchuljahrId == w.O.JahrVorb
                                            select new { f.SchuelerId, k.Name, kf.FachId, f.ErfasstVonId }).ToListAsync());
        var wahl = Assert.Single(neu);                           // eleve (5e A, Français Pflicht) bekommt keine Wahl
        Assert.Equal((w.O.SchuelerZiel, "6e A", w.O.FachFr, w.D), (wahl.SchuelerId, wahl.Name, wahl.FachId, wahl.ErfasstVonId));
        Assert.Equal(2, await w.MitDbAsync(db => db.Fachwahlen.CountAsync(f => f.KlassenfachId == w.O.Kf6AFr))); // Vorjahr unverändert
    }

    [Fact]
    public async Task Abgeschlossenes_Jahr_sperrt_Schalter_und_Wahl()
    {
        var w = await MitWahlfachAsync();
        var u = w.O.D.Unterricht;
        await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]);
        await w.AendernAsync(async db => (await db.Schuljahre.SingleAsync(j => j.Id == w.O.Jahr)).Status = SchuljahrStatus.Abgeschlossen);
        await Assert.ThrowsAsync<FachFehler>(() => u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, []));
        await Assert.ThrowsAsync<FachFehler>(() => u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, false, WahlfachRegel.PunkteUeber10));
        var ansicht = await u.FachwahlenAsync(w.D, w.O.Klasse6A);
        Assert.False(ansicht.Offen);
        Assert.Equal(1, await w.MitDbAsync(db => db.Fachwahlen.CountAsync()));
    }
}
