using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Noten;

namespace Skoulou.Tests.Noten;

/// <summary>Assistent « Passage à l'année suivante »: Vorschlag, Sperre bei offenen Entscheidungen, Ausführung, Idempotenz, Rechte.</summary>
public class JahreswechselTests
{
    private static Task EntscheidenAsync(NotenWelt w, Guid einschreibung, JahresEntscheidungsArt art)
        => w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, einschreibung, new EntscheidungEingabe(art, null, null));

    [Fact]
    public async Task Vorschlag_Folgestufe_mit_gleichem_Suffix()
    {
        var w = await NotenWelt.ErstelleAsync();
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        Assert.Equal("2027-2028", plan.Bezeichnung);
        Assert.Equal(w.O.Klasse5A, plan.ZielAdmis[w.O.Klasse6A]);
        Assert.Equal(w.O.Klasse5A, plan.ZielAdmis[w.O.Klasse6B]);
        Assert.Null(plan.ZielAdmis[w.O.Klasse5A]);
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(w.O.JahrVorb, v.NeuesJahrId);
        Assert.Equal(2, v.Offen);
        Assert.False(v.Bereit);
        Assert.Contains(v.Warnungen, x => x.Contains("3e trimestre"));
    }

    [Fact]
    public async Task Offene_Entscheidungen_blockieren_bis_sie_gesetzt_sind()
    {
        var w = await NotenWelt.ErstelleAsync();
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        Assert.Contains("décision", (await Assert.ThrowsAsync<FachFehler>(() => w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan))).Message);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        Assert.Equal(1, await w.Wechsel.OffeneEntscheidenAsync(w.D, w.O.Klasse6A, JahresEntscheidungsArt.Admis));
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.True(v.Bereit);
        Assert.Equal(2, v.Eingeschrieben);
    }

    [Fact]
    public async Task Ausfuehrung_legt_Klassen_Faecher_Einschreibungen_an_und_schliesst_das_Jahr()
    {
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        plan.Namen[w.O.Klasse6B] = "6e B bis";
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Wechsel.AusfuehrenAsync(w.O.Lehrer, w.O.Jahr, plan));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Wechsel.AusfuehrenAsync(w.O.DirektorB, w.O.Jahr, plan));

        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.O.Koordinator, w.O.Jahr, plan);
        Assert.Equal((w.O.JahrVorb, 3, 2, 0), (ergebnis.NeuesJahrId, ergebnis.NeueKlassen, ergebnis.Einschreibungen, ergebnis.Sortants));

        var klassen = await w.MitDbAsync(db => db.Klassen.Where(k => k.SchuljahrId == w.O.JahrVorb).ToDictionaryAsync(k => k.Name));
        Assert.Equal(["5e A", "6e A", "6e B bis"], klassen.Keys.Order());
        var e1 = await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.SchuelerId == w.O.Schueler && e.SchuljahrId == w.O.JahrVorb));
        Assert.Equal(klassen["5e A"].Id, e1.KlasseId);
        Assert.False(e1.IstWiederholer);
        var e2 = await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.SchuelerId == w.O.SchuelerZiel && e.SchuljahrId == w.O.JahrVorb));
        Assert.Equal(klassen["6e A"].Id, e2.KlasseId);
        Assert.True(e2.IstWiederholer);
        Assert.Equal(EinschreibungsStatus.Terminee, (await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.Id == w.E1))).Status);

        var faecher = await w.MitDbAsync(db => db.Klassenfaecher.Where(kf => kf.KlasseId == klassen["6e A"].Id).ToListAsync());
        Assert.Equal(3m, faecher.Single(f => f.FachId == w.O.FachMath).Koeffizient);
        Assert.Equal(2, faecher.Count);
        var mathNeu = faecher.Single(f => f.FachId == w.O.FachMath).Id;
        Assert.True(await w.MitDbAsync(db => db.Lehrauftraege.AnyAsync(l => l.KlassenfachId == mathNeu && l.LehrerId == w.O.Lehrer)));

        var jahr = await w.MitDbAsync(db => db.Schuljahre.SingleAsync(j => j.Id == w.O.Jahr));
        Assert.Equal(SchuljahrStatus.Abgeschlossen, jahr.Status);
        Assert.All(await w.MitDbAsync(db => db.Perioden.Where(p => p.SchuljahrId == w.O.Jahr).ToListAsync()), p => Assert.Equal(PeriodenStatus.Geschlossen, p.Status));
        Assert.All(await w.MitDbAsync(db => db.Jahresentscheidungen.ToListAsync()), j => Assert.NotNull(j.AusgefuehrtAmUtc));
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == AuditAktionen.Jahreswechsel)));
        Assert.Equal(2, Assert.Single(w.Teilnehmer.Aufrufe).NeueEinschreibungen.Count);

        // Idempotent: ein zweiter Lauf ändert nichts.
        Assert.True((await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan)).BereitsAusgefuehrt);
        await Assert.ThrowsAsync<FachFehler>(() => w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan));
        Assert.Equal(2, await w.MitDbAsync(db => db.Einschreibungen.CountAsync(e => e.SchuljahrId == w.O.JahrVorb)));
        await Assert.ThrowsAsync<FachFehler>(() => EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Redouble));
    }

    [Fact]
    public async Task Austritte_und_neues_Jahr_mit_kopierter_Struktur()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db =>
        {
            db.Perioden.RemoveRange(await db.Perioden.Where(p => p.SchuljahrId == w.O.JahrVorb).ToListAsync());
            db.Schuljahre.Remove(await db.Schuljahre.SingleAsync(j => j.Id == w.O.JahrVorb));
        });
        await w.Bewertungen.KomponenteAnlegenAsync(w.D, w.O.P1, new KomponenteEingabe("Interrogation", BewertungsArt.Interrogation, 1, 20));
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Exclu);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Admis);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        Assert.Equal(new DateOnly(2027, 9, 1), plan.Beginn);
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal((1, 1), (ergebnis.Einschreibungen, ergebnis.Sortants));
        var perioden = await w.MitDbAsync(db => db.Perioden.Where(p => p.SchuljahrId == ergebnis.NeuesJahrId).OrderBy(p => p.Nr).ToListAsync());
        Assert.Equal(3, perioden.Count);
        Assert.Equal(new DateOnly(2027, 12, 18), perioden[0].Ende);
        Assert.Equal("Interrogation", await w.MitDbAsync(db => db.PeriodenKomponenten.Where(k => k.PeriodeId == perioden[0].Id).Select(k => k.Titel).SingleAsync()));
        var alt = await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.Id == w.E1));
        Assert.Equal(EinschreibungsStatus.Partie, alt.Status);
        Assert.Contains("exclu", alt.Austrittsgrund);
        Assert.Equal(SchuljahrStatus.Vorbereitung, await w.MitDbAsync(db => db.Schuljahre.Where(j => j.Id == ergebnis.NeuesJahrId).Select(j => j.Status).SingleAsync()));
    }

    [Fact]
    public async Task Session_Klassen_behalten_ihre_Session_im_neuen_Jahr()
    {
        // Gefunden in der Simulation (E7a): Sprachschulen verloren beim Jahreswechsel die Session ihrer Klassen.
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db => (await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse6B)).PeriodeId = w.O.P2);
        await w.Wechsel.OffeneEntscheidenAsync(w.D, w.O.Klasse6A, JahresEntscheidungsArt.Admis);
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, await w.Wechsel.PlanAsync(w.D, w.O.Jahr));

        var klassen = await w.MitDbAsync(db => db.Klassen.Where(k => k.SchuljahrId == ergebnis.NeuesJahrId).ToDictionaryAsync(k => k.Name, k => k.PeriodeId));
        Assert.Equal(w.O.PV2, klassen["6e B"]);
        Assert.Null(klassen["6e A"]);
    }
}
