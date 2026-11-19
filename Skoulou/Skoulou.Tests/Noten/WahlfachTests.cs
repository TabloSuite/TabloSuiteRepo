using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;

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
