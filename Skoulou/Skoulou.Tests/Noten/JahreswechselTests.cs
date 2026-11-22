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
    public async Task Klassennamen_mit_Leerzeichen_brechen_den_Jahreswechsel_nicht()
    {
        // Klicktest 21.11.2026: neuer Name « 5e  A » (doppeltes/abschließendes Leerzeichen) → KeyNotFoundException, Seite abgestürzt.
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        plan.Namen[w.O.Klasse5A] = " 5e  A ";
        plan.Namen[w.O.Klasse6A] = "6e A  ";
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(2, ergebnis.Einschreibungen);
        var e1 = await w.MitDbAsync(db => db.Einschreibungen.Where(e => e.SchuelerId == w.O.Schueler && e.SchuljahrId == w.O.JahrVorb)
            .Join(db.Klassen, e => e.KlasseId, k => k.Id, (e, k) => k.Name).SingleAsync());
        Assert.StartsWith("5e", e1);
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

    [Fact]
    public async Task Vergessener_Session_Teilnehmer_blockiert_den_Jahreswechsel_nicht()
    {
        // Gefunden in der Simulation (S3): wer in einer Session-Klasse ohne Wechsel/Austritt blieb, stand ohne Entscheidung in
        // der Liste des Assistenten und blockierte den Jahreswechsel der Sprachschulen.
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db =>
        {
            (await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse6B)).PeriodeId = w.O.P1;
            (await db.Einschreibungen.SingleAsync(e => e.Id == w.E2)).KlasseId = w.O.Klasse6B;
        });
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.True(v.Bereit);
        Assert.DoesNotContain(v.Klassen.SelectMany(k => k.Schueler), s => s.EinschreibungId == w.E2);
        // « Bulletins non publiés » zählt die Klasse der früheren Session nicht mit (es bleiben 6e A und 5e A).
        Assert.Contains(v.Warnungen, x => x.StartsWith("2 classe(s)", StringComparison.Ordinal));

        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(1, ergebnis.Einschreibungen);
        var alt = await w.MitDbAsync(db => db.Einschreibungen.SingleAsync(e => e.Id == w.E2));
        Assert.Equal((EinschreibungsStatus.Terminee, (DateOnly?)new DateOnly(2026, 12, 18)), (alt.Status, alt.Bis));
        Assert.False(await w.MitDbAsync(db => db.Einschreibungen.AnyAsync(e => e.SchuelerId == w.O.SchuelerZiel && e.SchuljahrId == ergebnis.NeuesJahrId)));
    }
    [Fact]
    public async Task Admis_ohne_Klasse_im_neuen_Jahr_werden_gewarnt_und_muessen_bestaetigt_werden()
    {
        // Fehlersuche 21.11.2026 (Klassen ohne Folgeklasse schickten « Admis » still als « Fin de cycle » aus der Schule),
        // Nutzerentscheidung 22.11.: deutlich warnen und vor « Effectuer » bestätigen lassen – auch serverseitig.
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        plan.ZielAdmis[w.O.Klasse6A] = null;

        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(1, v.AdmisOhneKlasse);
        Assert.Contains(v.Warnungen, x => x.Contains("admis n'ont pas de classe") && x.Contains("6e A"));
        Assert.True(v.Bereit);

        var fehler = await Assert.ThrowsAsync<FachFehler>(() => w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan));
        Assert.Contains("confirmez", fehler.Message);
        Assert.Equal(0, await w.MitDbAsync(db => db.Einschreibungen.CountAsync(e => e.SchuljahrId == w.O.JahrVorb)));

        plan.AbgaengeBestaetigt = true;
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal((1, 1), (ergebnis.Einschreibungen, ergebnis.Sortants));
    }

    [Fact]
    public async Task Deaktivierte_Klasse_des_Folgejahres_wird_nicht_still_benutzt()
    {
        // Fehlersuche 21.11.2026 (Z3), Nutzerentscheidung 22.11.: eine deaktivierte « 5e A » im neuen Jahr nahm die Admis auf.
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db =>
        {
            var vorlage = await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse5A);
            db.Klassen.Add(new Klasse { SchuleId = vorlage.SchuleId, SchuljahrId = w.O.JahrVorb, StufeId = vorlage.StufeId, Name = "5e A", Aktiv = false });
        });
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);

        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.Contains(v.Warnungen, x => x.Contains("« 5e A »") && x.Contains("désactivée"));
        var fehler = await Assert.ThrowsAsync<FachFehler>(() => w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan));
        Assert.Contains("désactivée", fehler.Message);

        // Anderer Name → neue Klasse, die deaktivierte bleibt unberührt
        plan.Namen[w.O.Klasse5A] = "5e A bis";
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(2, ergebnis.Einschreibungen);
        Assert.False(await w.MitDbAsync(db => db.Klassen.Where(k => k.SchuljahrId == w.O.JahrVorb && k.Name == "5e A").Select(k => k.Aktiv).SingleAsync()));
    }
}
