using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.Tests.Organisation;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Noten;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.Tests.Noten;

/// <summary>Assistent « Passage à l'année suivante »: Vorschlag, Sperre bei offenen Entscheidungen, Ausführung, Idempotenz, Rechte.</summary>
public class JahreswechselTests
{
    private static Task EntscheidenAsync(NotenWelt w, Guid einschreibung, JahresEntscheidungsArt art)
        => w.Conseil.EntscheidungSpeichernAsync(w.D, w.O.P3, einschreibung, new EntscheidungEingabe(art, null, null));

    /// <summary>Plan des Assistenten; die Testuhr (13.11.2026) liegt vor dem Jahresende → Bestätigung gesetzt (22.11.2026).</summary>
    private static async Task<PassagePlan> PlanAsync(NotenWelt w)
    {
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        plan.VorJahresendeBestaetigt = true;
        return plan;
    }

    [Fact]
    public async Task Vorschlag_Folgestufe_mit_gleichem_Suffix()
    {
        var w = await NotenWelt.ErstelleAsync();
        var plan = await PlanAsync(w);
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
        var plan = await PlanAsync(w);
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
        var plan = await PlanAsync(w);
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
        var plan = await PlanAsync(w);
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
        var plan = await PlanAsync(w);
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
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, await PlanAsync(w));

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
        var plan = await PlanAsync(w);
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.True(v.Bereit);
        Assert.DoesNotContain(v.Klassen.SelectMany(k => k.Schueler), s => s.EinschreibungId == w.E2);
        // « Bulletins non publiés » zählt die Klasse der früheren Session nicht mit und – seit 23.11.2026 – auch keine Klasse ohne
        // Schüler am Ende der letzten Periode (5e A ist hier leer und könnte nie veröffentlichen): es bleibt nur 6e A.
        Assert.Contains(v.Warnungen, x => x.StartsWith("1 classe(s)", StringComparison.Ordinal));

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
        var plan = await PlanAsync(w);
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
    public async Task Ueberbelegte_Zielklasse_wird_mit_Zahlen_gewarnt_aber_nicht_gesperrt()
    {
        // Nutzer 22.11.2026 (Normaltest: 6e A + 6e B → 5e A mit 48 Schülern bei Kapazität 35, ohne Hinweis).
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db => (await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse5A)).Kapazitaet = 1);
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Admis);
        var plan = await PlanAsync(w);

        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        var u = Assert.Single(v.Ueberbelegt);
        Assert.Equal(("5e A", 0, 2, 1), (u.Klasse, u.Bestand, u.Zugaenge, u.Kapazitaet));
        Assert.Contains(v.Warnungen, x => x.Contains("« 5e A » (2027-2028) aura 2 élèves pour une capacité de 1, soit 1 de trop"));
        Assert.True(v.Bereit);

        // Schon im neuen Jahr eingeschriebene Schüler der vorhandenen Klasse zählen mit (ihre Kapazität gilt).
        await w.AendernAsync(async db =>
        {
            var vorlage = await db.Klassen.SingleAsync(k => k.Id == w.O.Klasse5A);
            var neu = new Klasse { SchuleId = vorlage.SchuleId, SchuljahrId = w.O.JahrVorb, StufeId = vorlage.StufeId, Name = "5e A", Kapazitaet = 2 };
            db.Klassen.Add(neu);
            db.Einschreibungen.Add(new Einschreibung
            {
                SchuleId = vorlage.SchuleId, SchuelerId = w.O.SchuelerOhne, SchuljahrId = w.O.JahrVorb, KlasseId = neu.Id,
                Von = new DateOnly(2027, 9, 1), Status = EinschreibungsStatus.Active
            });
        });
        v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        u = Assert.Single(v.Ueberbelegt);
        Assert.Equal((1, 2, 2, 3), (u.Bestand, u.Zugaenge, u.Kapazitaet, u.Gesamt));
        Assert.Contains(v.Warnungen, x => x.Contains("3 élèves pour une capacité de 2") && x.Contains("(2 par le passage et 1 déjà inscrit(s))"));

        // Nur eine Warnung: die Ausführung bleibt möglich.
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(2, ergebnis.Einschreibungen);
    }

    [Fact]
    public async Task Konten_der_Abgaenger_werden_archiviert_Eltern_nicht()
    {
        // Nutzer 22.11.2026 (Normaltest: ehemalige Schüler meldeten sich nach dem Passage weiter an).
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Exclu);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Admis);
        var vorher = await w.MitDbAsync(db => db.Users.Where(k => k.Rolle == Rollen.Eltern).ToDictionaryAsync(k => k.Id, k => k.Status));
        var plan = await PlanAsync(w);
        Assert.True(plan.AbgaengerArchivieren);

        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal((1, 1, 1), (ergebnis.Einschreibungen, ergebnis.Sortants, ergebnis.Archiviert));
        var schueler = await w.MitDbAsync(db => db.Users.SingleAsync(k => k.Id == w.O.Schueler));
        Assert.Equal(KontoStatus.Archive, schueler.Status);
        Assert.Equal(KontoStatus.Actif, await w.MitDbAsync(db => db.Users.Where(k => k.Id == w.O.SchuelerZiel).Select(k => k.Status).SingleAsync()));
        Assert.Equal(vorher, await w.MitDbAsync(db => db.Users.Where(k => k.Rolle == Rollen.Eltern).ToDictionaryAsync(k => k.Id, k => k.Status)));
        Assert.True(await w.MitDbAsync(db => db.AuditEintraege.AnyAsync(a => a.Aktion == AuditAktionen.KontoArchiviert && a.ZielId == w.O.Schueler)));
        // Daten bleiben: alte Einschreibung (mit Noten/Bulletins) unverändert vorhanden; Anmeldung mit eigener Meldung abgewiesen.
        Assert.Equal(EinschreibungsStatus.Partie, await w.MitDbAsync(db => db.Einschreibungen.Where(e => e.Id == w.E1).Select(e => e.Status).SingleAsync()));
        Assert.Equal(AnmeldeErgebnis.KontoArchiviert, AnmeldeRegeln.Pruefe(schueler.Status, schueler.Rolle, SchulStatus.Active));

        // Wieder aktivierbar (« Réintégrer »), dann einschreibbar.
        await w.O.D.Konten.ReintegrierenAsync(w.D, w.O.Schueler);
        Assert.Equal(KontoStatus.Actif, await w.MitDbAsync(db => db.Users.Where(k => k.Id == w.O.Schueler).Select(k => k.Status).SingleAsync()));
    }

    [Fact]
    public async Task Passage_vor_dem_Jahresende_wird_gewarnt_und_muss_bestaetigt_werden()
    {
        // Nutzer 22.11.2026 (Nachtest: Schule D schloss ihr Jahr am 20.03. – vor Beginn des 3. Trimesters).
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        Assert.False(plan.VorJahresendeBestaetigt);

        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.Equal(new DateOnly(2027, 6, 30), v.JahrLaeuftBis);
        Assert.StartsWith("L'année 2026-2027 n'est pas terminée : sa dernière période, « 3e trimestre », se termine le 30/06/2027.", v.Warnungen[0]);
        Assert.True(v.Bereit);   // nur Warnung + Bestätigung, keine Sperre der Seite
        var f = await Assert.ThrowsAsync<FachFehler>(() => w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan));
        Assert.Contains("n'est pas terminée (fin de « 3e trimestre » le 30/06/2027)", f.Message);
        Assert.Equal(0, await w.MitDbAsync(db => db.Einschreibungen.CountAsync(e => e.SchuljahrId == w.O.JahrVorb)));

        plan.VorJahresendeBestaetigt = true;
        Assert.Equal(2, (await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan)).Einschreibungen);
    }

    [Fact]
    public async Task Nach_dem_Jahresende_ohne_Warnung_und_ohne_Bestaetigung()
    {
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Admis);
        w.Zeit.Jetzt = new DateTimeOffset(2027, 7, 1, 8, 0, 0, TimeSpan.Zero);   // 01/07/2027 in Lomé
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.Null(v.JahrLaeuftBis);
        Assert.DoesNotContain(v.Warnungen, x => x.Contains("n'est pas terminée"));
        Assert.Equal(2, (await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan)).Einschreibungen);
    }

    [Fact]
    public async Task Massgeblich_ist_das_Ende_der_letzten_zaehlenden_Periode_nicht_Fin()
    {
        // Nutzer 22.11.2026 (R7): « Fin » des Schuljahres am 31/07, 3e trimestre bis 30/06, danach ein Examen blanc (zählt nicht).
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Admis);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Admis);
        await w.AendernAsync(async db =>
        {
            (await db.Schuljahre.SingleAsync(j => j.Id == w.O.Jahr)).Ende = new DateOnly(2027, 7, 31);
            db.Perioden.Add(new Periode
            {
                SchuleId = w.O.SchuleA, SchuljahrId = w.O.Jahr, Nr = 4, Titel = "Examen blanc", Beginn = new(2027, 7, 5), Ende = new(2027, 7, 20),
                ZaehltFuerJahr = false, Jahresgewicht = 0
            });
        });

        w.Zeit.Jetzt = new DateTimeOffset(2027, 6, 15, 8, 0, 0, TimeSpan.Zero);
        var plan = await w.Wechsel.PlanAsync(w.D, w.O.Jahr);
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.Equal((new DateOnly(2027, 6, 30), "3e trimestre"), (v.JahrLaeuftBis, v.JahrLaeuftBisPeriode));
        Assert.Contains("« 3e trimestre », se termine le 30/06/2027", v.Warnungen[0]);

        // Nach dem 3e trimestre, aber vor « Fin » (31/07) und vor dem Examen blanc: keine Warnung, keine Bestätigung.
        w.Zeit.Jetzt = new DateTimeOffset(2027, 7, 1, 8, 0, 0, TimeSpan.Zero);
        v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, plan);
        Assert.False(v.VorJahresende);
        Assert.DoesNotContain(v.Warnungen, x => x.Contains("n'est pas terminée"));
        Assert.Equal(2, (await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan)).Einschreibungen);
    }

    [Fact]
    public async Task Ohne_zaehlende_Perioden_gilt_Fin_des_Schuljahres()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db =>
        {
            (await db.Schuljahre.SingleAsync(j => j.Id == w.O.Jahr)).Ende = new DateOnly(2027, 7, 31);
            foreach (var p in await db.Perioden.Where(p => p.SchuljahrId == w.O.Jahr).ToListAsync()) p.ZaehltFuerJahr = false;
        });
        w.Zeit.Jetzt = new DateTimeOffset(2027, 7, 1, 8, 0, 0, TimeSpan.Zero);
        var v = await w.Wechsel.VorschauAsync(w.D, w.O.Jahr, await w.Wechsel.PlanAsync(w.D, w.O.Jahr));
        Assert.Equal((new DateOnly(2027, 7, 31), null), (v.JahrLaeuftBis, v.JahrLaeuftBisPeriode));
        Assert.StartsWith("L'année 2026-2027 n'est pas terminée : elle se termine le 31/07/2027.", v.Warnungen[0]);
        Assert.Equal("fin le 31/07/2027", v.JahresendeText);
    }

    [Fact]
    public async Task Ohne_Schalter_bleiben_die_Konten_der_Abgaenger_aktiv()
    {
        var w = await NotenWelt.ErstelleAsync();
        await EntscheidenAsync(w, w.E1, JahresEntscheidungsArt.Partie);
        await EntscheidenAsync(w, w.E2, JahresEntscheidungsArt.Redouble);
        var plan = await PlanAsync(w);
        plan.AbgaengerArchivieren = false;
        var ergebnis = await w.Wechsel.AusfuehrenAsync(w.D, w.O.Jahr, plan);
        Assert.Equal((1, 0), (ergebnis.Sortants, ergebnis.Archiviert));
        Assert.Equal(KontoStatus.Actif, await w.MitDbAsync(db => db.Users.Where(k => k.Id == w.O.Schueler).Select(k => k.Status).SingleAsync()));
    }

    [Fact]
    public async Task Archiviertes_Konto_wird_vor_dem_Einschreiben_reintegriert()
    {
        var w = await NotenWelt.ErstelleAsync();
        await w.AendernAsync(async db => (await db.Users.SingleAsync(k => k.Id == w.O.SchuelerOhne)).Status = KontoStatus.Archive);
        var f = await Assert.ThrowsAsync<FachFehler>(() => w.O.D.Einschreibung.EinschreibenAsync(w.D, w.O.SchuelerOhne, w.O.Klasse6A, OrgWelt.Heute, false));
        Assert.Contains("réintégrez-le d'abord", f.Message);
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
        var plan = await PlanAsync(w);

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
