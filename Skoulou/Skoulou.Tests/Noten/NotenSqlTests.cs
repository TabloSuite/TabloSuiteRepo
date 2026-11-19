using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Tests.Organisation;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Nur mit SKOULOU_SQL_TESTS=1: Unique-Indizes (Note, Bulletinnummer), parallele Notenänderungen, Veröffentlichung auf SQL Server.</summary>
public class NotenSqlTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private Task<NotenWelt> WeltAsync() => NotenWelt.ErstelleAsync(new SqlFabrik(datenbank));

    [SqlFact]
    public async Task Eindeutige_Indizes_Note_und_Bulletinnummer()
    {
        var w = await WeltAsync();
        var b = await w.BewertungAsync(w.O.Kf6AMath);
        await w.NoteAsync(b, w.E1, "12");
        await using (var db = datenbank.Kontext())
        {
            db.Noten.Add(new Note { SchuleId = w.O.SchuleA, BewertungId = b, EinschreibungId = w.E1, Punkte = 5, ErfasstVonId = w.D });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await w.VollstaendigAsync();
        await w.VeroeffentlichenAsync(trotz: true);
        var vorhanden = await w.MitDbAsync(db => db.Bulletins.FirstAsync());
        await using (var db = datenbank.Kontext())
        {
            db.Bulletins.Add(new Bulletin
            {
                SchuleId = w.O.SchuleA, PeriodeId = w.O.P2, EinschreibungId = w.E1, Nummer = vorhanden.Nummer, PruefToken = new string('a', 64),
                Status = BulletinStatus.Publie, SnapshotJson = "{}", Hash = "x", VeroeffentlichtAmUtc = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // Höchstens ein gültiges Bulletin je Periode und Einschreibung.
        await using (var db = datenbank.Kontext())
        {
            db.Bulletins.Add(new Bulletin
            {
                SchuleId = w.O.SchuleA, PeriodeId = w.O.P1, EinschreibungId = w.E1, Nummer = "BUL-2026-99999", PruefToken = new string('b', 64),
                Status = BulletinStatus.Publie, SnapshotJson = "{}", Hash = "x", VeroeffentlichtAmUtc = DateTime.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        Assert.Equal(["BUL-2026-00001", "BUL-2026-00002"], await w.MitDbAsync(db => db.Bulletins.OrderBy(x => x.Nummer).Select(x => x.Nummer).ToListAsync()));
    }

    [SqlFact]
    public async Task Parallele_Notenaenderungen_ergeben_eine_lueckenlose_Historie()
    {
        var w = await WeltAsync();
        var b = await w.BewertungAsync(w.O.Kf6AMath);
        var werte = Enumerable.Range(1, 6).Select(i => (i + 10).ToString()).ToList();
        await Task.WhenAll(werte.Select(v => Task.Run(() => w.NoteAsync(b, w.E1, v))));
        var note = await w.MitDbAsync(db => db.Noten.SingleAsync(n => n.BewertungId == b && n.EinschreibungId == w.E1));
        var historie = await w.MitDbAsync(db => db.NotenAenderungen.Where(h => h.NoteId == note.Id).ToListAsync());
        Assert.Equal(6, historie.Count);
        // Kette alt → neu ohne Lücken und ohne Verzweigung (jede Änderung baut auf der vorherigen auf).
        var aktuell = Assert.Single(historie, h => h.AltePunkte is null);
        for (var i = 1; i < historie.Count; i++) aktuell = Assert.Single(historie, h => h.AltePunkte == aktuell.NeuePunkte);
        Assert.Equal(aktuell.NeuePunkte, note.Punkte);
    }

    [SqlFact]
    public async Task Abfragen_der_Seiten_sind_uebersetzbar()
    {
        var w = await WeltAsync();
        await w.VollstaendigAsync();
        Assert.NotEmpty((await w.Noten.AuswahlAsync(w.D, w.O.Jahr)).Klassen);
        Assert.Equal(2, (await w.Noten.RasterAsync(w.D, w.O.Kf6AMath, w.O.P1)).Zeilen.Count);
        await w.VeroeffentlichenAsync();
        Assert.Equal(2, (await w.Lesen.SucheAsync(w.D, w.O.Jahr, null, null, "test", null, 0)).Eintraege.Count);
        Assert.Single(await w.Lesen.EigeneAsync(w.O.Eltern, w.O.Schueler));
        Assert.NotEmpty(await w.Lesen.VollstaendigkeitAsync(w.D, w.O.Jahr));
        Assert.NotEmpty(await w.Bewertungen.KomponentenAsync(w.D, w.O.Jahr));
        await w.Bulletins.RectificationOeffnenAsync(w.D, w.O.Klasse6A, w.O.P1, "Contrôle");
        Assert.Empty((await w.Bulletins.RectificationVorschauAsync(w.D, w.O.Klasse6A, w.O.P1)).Aenderungen);
        await w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, w.E1, new EntscheidungEingabe(JahresEntscheidungsArt.Admis, null, null));
        await w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, w.E2, new EntscheidungEingabe(JahresEntscheidungsArt.Redouble, null, null));
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(2, ergebnis.Einschreibungen);
    }

    /// <summary>« Matières facultatives »: eindeutige Wahl je (Klassenfach, Schüler) und Übersetzbarkeit aller neuen Abfragen.</summary>
    [SqlFact]
    public async Task Wahlfaecher_eindeutiger_Index_und_Abfragen()
    {
        var w = await WeltAsync();
        var u = w.O.D.Unterricht;
        await u.WahlfachSetzenAsync(w.D, w.O.Kf6AFr, true, WahlfachRegel.PunkteUeber10);
        Assert.Equal(1, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]));
        await using (var db = datenbank.Kontext())
        {
            db.Fachwahlen.Add(new Fachwahl { SchuleId = w.O.SchuleA, KlassenfachId = w.O.Kf6AFr, SchuelerId = w.O.Schueler, ErfasstVonId = w.D });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = datenbank.Kontext())
        {
            // anderer Schüler im selben Wahlfach und derselbe Schüler in einem anderen Klassenfach sind erlaubt
            db.Fachwahlen.Add(new Fachwahl { SchuleId = w.O.SchuleA, KlassenfachId = w.O.Kf6AFr, SchuelerId = w.O.SchuelerZiel, ErfasstVonId = w.D });
            db.Fachwahlen.Add(new Fachwahl { SchuleId = w.O.SchuleA, KlassenfachId = w.O.Kf6AMath, SchuelerId = w.O.Schueler, ErfasstVonId = w.D });
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await u.FachwahlenSpeichernAsync(w.D, w.O.Klasse6A, [(w.O.Kf6AFr, w.O.Schueler)]));

        var math = await w.BewertungAsync(w.O.Kf6AMath);
        var fr = await w.BewertungAsync(w.O.Kf6AFr);
        await w.NoteAsync(math, w.E1, "15");
        await w.NoteAsync(math, w.E2, "11");
        await w.NoteAsync(fr, w.E1, "16");
        Assert.Single((await w.Noten.RasterAsync(w.D, w.O.Kf6AFr, w.O.P1)).Zeilen);
        Assert.Equal(2, (await u.KlassenfaecherAsync(w.D, w.O.Klasse6A)).Count);
        Assert.Equal(1, (await w.O.D.Klassen.KlassenAsync(w.D, w.O.Jahr)).Single(k => k.Id == w.O.Klasse6A).AnzahlWahlfaecher);
        Assert.Equal(3, (await w.Lesen.VollstaendigkeitAsync(w.D, w.O.Jahr, w.O.P1)).Single(k => k.KlasseId == w.O.Klasse6A).Erwartet);
        var uebersicht = await w.Conseil.UebersichtAsync(w.D, w.O.Klasse6A, w.O.P1);
        Assert.Equal(19m, uebersicht.Zeilen.Single(z => z.EinschreibungId == w.E1).Moyenne.Wert);
        await w.AendernAsync(async db => (await db.Schulen.SingleAsync(s => s.Id == w.O.SchuleA)).ElternSehenNotenLive = true);
        Assert.Equal(2, (await w.Lesen.LiveNotenAsync(w.O.Eltern, w.O.Schueler, w.O.P1)).Faecher.Count);
        await w.O.D.Unterricht.FaecherHinzufuegenAsync(w.D, w.O.Klasse6B, [w.O.FachFr]);
        await u.KoeffizientenKopierenAsync(w.D, w.O.Klasse6A, w.O.Klasse6B);
        await w.O.D.Einschreibung.KlasseWechselnAsync(w.D, w.O.Schueler, w.O.Klasse6B, new(2026, 11, 2));
        Assert.Equal(2, await w.MitDbAsync(db => db.Fachwahlen.CountAsync(x => x.SchuelerId == w.O.Schueler && x.KlassenfachId != w.O.Kf6AMath)));
    }
}
