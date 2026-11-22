using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Organisation;
using Skoulou.WebApp.Services.Organisation;

namespace Skoulou.Tests.Organisation;

/// <summary>Schuljahr und Perioden: keine Überlappung, eindeutige Bezeichnung, ein laufendes Jahr, eingefrorener Preis, Abschluss/Wiedereröffnung.</summary>
public class SchuljahrRegelnTests
{
    [Fact]
    public void Start_Rueckfrage_nennt_den_Preis()
    {
        // Nutzer 22.11.2026: ohne Rechnung den Preis, der jetzt eingefroren wird; nach einer Rechnung den festgehaltenen Preis.
        Assert.Contains("Le prix SKOULOU par élève et par mois (450 FCFA) est figé pour toute l'année", SchuljahrService.StartFrage(null, 450));
        var nachRechnung = SchuljahrService.StartFrage(500, 450);
        Assert.Contains("Le prix SKOULOU de 500 FCFA par élève et par mois, déjà figé par la première facture", nachRechnung);
        Assert.Contains("450 FCFA, ne s'applique pas", nachRechnung);
        Assert.DoesNotContain("ne s'applique pas", SchuljahrService.StartFrage(500, 500));
        Assert.EndsWith("ne pourront plus être ajoutées ni supprimées.", nachRechnung);
    }

    [Theory]
    [InlineData(PeriodenArt.Trimestre, 7, 3)]
    [InlineData(PeriodenArt.Semestre, 5, 2)]
    [InlineData(PeriodenArt.Session, 5, 5)]
    [InlineData(PeriodenArt.Libre, 1, 1)]
    public void Periodenstruktur_teilt_das_Jahr_lueckenlos(PeriodenArt art, int anzahl, int erwartet)
    {
        var beginn = new DateOnly(2026, 9, 1);
        var ende = new DateOnly(2027, 6, 30);
        var perioden = PeriodenPlan.Erzeuge(art, anzahl, beginn, ende);
        Assert.Equal(erwartet, perioden.Count);
        Assert.Equal(beginn, perioden[0].Beginn);
        Assert.Equal(ende, perioden[^1].Ende);
        for (var i = 1; i < perioden.Count; i++) Assert.Equal(perioden[i - 1].Ende.AddDays(1), perioden[i].Beginn);
        Assert.Equal(Enumerable.Range(1, erwartet), perioden.Select(p => p.Nr));
        if (art == PeriodenArt.Trimestre) Assert.Equal(new[] { "1er trimestre", "2e trimestre", "3e trimestre" }, perioden.Select(p => p.Titel).ToArray());
    }

    [Fact]
    public void Eine_Periode_ist_Libre_mit_Anzahl_1_und_eigenem_Titel()
    {
        // 19.11.2026: « 1 période (un seul bulletin à la fin) » ohne neuen Enum-Wert.
        var perioden = PeriodenPlan.Erzeuge(PeriodenArt.Libre, 1, new(2026, 9, 1), new(2027, 6, 30));
        Assert.Equal(PeriodenPlan.EinePeriodeTitel, Assert.Single(perioden).Titel);
        Assert.Equal("MOYENNE DE L'ANNÉE SCOLAIRE", Skoulou.Core.Noten.NotenTexte.MoyenneTitel(perioden[0].Titel));
        Assert.True(PeriodenPlan.IstEinePeriode(PeriodenArt.Libre, 1));
        Assert.False(PeriodenPlan.IstEinePeriode(PeriodenArt.Session, 1));
        Assert.Equal("1 période", PeriodenPlan.Bezeichnung(PeriodenArt.Libre, 1));
        Assert.Equal("Périodes libres", PeriodenPlan.Bezeichnung(PeriodenArt.Libre, 4));
        Assert.Equal("Trimestres", PeriodenPlan.Bezeichnung(PeriodenArt.Trimestre, 3));
        Assert.Equal(["Période 1", "Période 2"], PeriodenPlan.Erzeuge(PeriodenArt.Libre, 2, new(2026, 9, 1), new(2027, 6, 30)).Select(p => p.Titel));
    }

    [Fact]
    public async Task Gueltiger_Monatspreis_der_Schule_fuer_den_Hinweis()
    {
        var w = await OrgWelt.ErstelleAsync();
        Assert.Equal(1_250, await w.D.Jahre.MonatspreisAsync(w.Direktor));
        await w.MitDbAsync(async db =>
        {
            db.Schulen.Single(s => s.Id == w.SchuleA).MonatspreisOverride = 400;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(400, await w.D.Jahre.MonatspreisAsync(w.Koordinator));
        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Jahre.MonatspreisAsync(w.Lehrer));
    }

    [Fact]
    public void Periodenpruefung_erlaubt_Examen_blanc_innerhalb_einer_Periode()
    {
        var jahr = new Zeitraum(new(2026, 9, 1), new(2027, 6, 30));
        var t1 = ("1er trimestre", new Zeitraum(new(2026, 9, 1), new(2026, 12, 18)), true);
        Assert.Null(PeriodenPlan.Pruefe(jahr, [t1, ("Examen blanc", new Zeitraum(new(2026, 12, 1), new(2026, 12, 5)), false)]));
        Assert.Contains("chevauchent", PeriodenPlan.Pruefe(jahr, [t1, ("2e trimestre", new Zeitraum(new(2026, 12, 18), new(2027, 3, 1)), true)]));
        Assert.Contains("dans l'année scolaire", PeriodenPlan.Pruefe(jahr, [("Hors", new Zeitraum(new(2026, 8, 1), new(2026, 9, 10)), true)]));
        Assert.Contains("chevauchent", PeriodenPlan.Pruefe(jahr, [t1, ("EB1", new Zeitraum(new(2026, 12, 1), new(2026, 12, 5)), false),
            ("EB2", new Zeitraum(new(2026, 12, 4), new(2026, 12, 9)), false)]));
    }

    [Fact]
    public async Task Jahr_anlegen_mit_Perioden_ohne_Ueberlappung_und_mit_eindeutigem_Namen()
    {
        var w = await OrgWelt.ErstelleAsync();
        var id = await w.D.Jahre.JahrAnlegenAsync(w.Direktor, new SchuljahrEingabe("2028-2029", new(2028, 9, 1), new(2029, 6, 30), PeriodenArt.Semestre, 9));
        var jahr = (await w.D.Jahre.JahreAsync(w.Direktor)).Single(j => j.Id == id);
        Assert.Equal(SchuljahrStatus.Vorbereitung, jahr.Status);
        Assert.Equal(2, jahr.Perioden.Count);
        Assert.Null(jahr.PlattformMonatspreis);

        var doppelt = await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrAnlegenAsync(w.Direktor,
            new SchuljahrEingabe("2028-2029", new(2029, 9, 1), new(2030, 6, 30), PeriodenArt.Trimestre, 3)));
        Assert.Equal("bezeichnung", doppelt.Feld);
        var ueberlappt = await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrAnlegenAsync(w.Direktor,
            new SchuljahrEingabe("2029-2030", new(2029, 6, 1), new(2030, 6, 30), PeriodenArt.Trimestre, 3)));
        Assert.Contains("chevauchent", ueberlappt.Message);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrAnlegenAsync(w.Direktor,
            new SchuljahrEingabe("2030-2031", new(2031, 6, 30), new(2030, 9, 1), PeriodenArt.Trimestre, 3)));
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.SchuljahrAngelegt)));
    }

    [Fact]
    public async Task Hoechstens_ein_laufendes_Jahr_und_Preis_wird_beim_Start_eingefroren()
    {
        var w = await OrgWelt.ErstelleAsync();
        var laufend = await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrStartenAsync(w.Direktor, w.JahrVorb));
        Assert.Contains("encore en cours", laufend.Message);

        await w.MitDbAsync(async db =>
        {
            db.PlattformEinstellungen.Single().MonatspreisProSchueler = 1_500;
            return await db.SaveChangesAsync();
        });
        await w.D.Jahre.JahrAbschliessenAsync(w.Koordinator, w.Jahr);
        await w.D.Jahre.JahrStartenAsync(w.Koordinator, w.JahrVorb);
        var vorb = await w.MitDbAsync(db => db.Schuljahre.SingleAsync(j => j.Id == w.JahrVorb));
        Assert.Equal(SchuljahrStatus.Laufend, vorb.Status);
        Assert.Equal(1_500, vorb.PlattformMonatspreis);

        // Spätere Preisänderungen ändern das laufende Jahr nicht; ein Schulpreis hat Vorrang beim nächsten Start.
        await w.MitDbAsync(async db =>
        {
            db.PlattformEinstellungen.Single().MonatspreisProSchueler = 2_000;
            db.Schulen.Single(s => s.Id == w.SchuleA).MonatspreisOverride = 900;
            return await db.SaveChangesAsync();
        });
        Assert.Equal(1_500, (await w.MitDbAsync(db => db.Schuljahre.SingleAsync(j => j.Id == w.JahrVorb))).PlattformMonatspreis);
        var neu = await w.D.Jahre.JahrAnlegenAsync(w.Direktor, new SchuljahrEingabe("2028-2029", new(2028, 9, 1), new(2029, 6, 30), PeriodenArt.Trimestre, 3));
        await w.D.Jahre.JahrAbschliessenAsync(w.Direktor, w.JahrVorb);
        await w.D.Jahre.JahrStartenAsync(w.Direktor, neu);
        Assert.Equal(900, (await w.MitDbAsync(db => db.Schuljahre.SingleAsync(j => j.Id == neu))).PlattformMonatspreis);
        Assert.Equal(2, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.SchuljahrGestartet)));
    }

    [Fact]
    public async Task Abschluss_schliesst_Perioden_Wiedereroeffnung_nur_Direktor_mit_Grund()
    {
        var w = await OrgWelt.ErstelleAsync();
        await w.D.Jahre.JahrAbschliessenAsync(w.Koordinator, w.Jahr);
        var jahr = (await w.D.Jahre.JahreAsync(w.Direktor)).Single(j => j.Id == w.Jahr);
        Assert.Equal(SchuljahrStatus.Abgeschlossen, jahr.Status);
        Assert.NotNull(jahr.AbgeschlossenAmUtc);
        Assert.All(jahr.Perioden, p => Assert.Equal(PeriodenStatus.Geschlossen, p.Status));

        // Geschlossenes Jahr: keine Änderungen mehr.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Klassen.KlasseSpeichernAsync(w.Direktor, w.Jahr, null, new KlasseEingabe("6e Z", null, null, null, null, null)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Einschreibung.EinschreibenAsync(w.Direktor, w.SchuelerOhne, w.Klasse6B, new(2026, 11, 2), false));

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.D.Jahre.JahrWiedereroeffnenAsync(w.Koordinator, w.Jahr, "Erreur"));
        var ohneGrund = await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrWiedereroeffnenAsync(w.Direktor, w.Jahr, "  "));
        Assert.Equal("grund", ohneGrund.Feld);
        await w.D.Jahre.JahrWiedereroeffnenAsync(w.Direktor, w.Jahr, "Notes du 3e trimestre à corriger");
        jahr = (await w.D.Jahre.JahreAsync(w.Direktor)).Single(j => j.Id == w.Jahr);
        Assert.Equal(SchuljahrStatus.Laufend, jahr.Status);
        Assert.Null(jahr.AbgeschlossenAmUtc);
        Assert.Equal("Notes du 3e trimestre à corriger", jahr.WiedereroeffnetGrund);
        var audit = await w.MitDbAsync(db => db.AuditEintraege.Select(a => a.Aktion).ToListAsync());
        Assert.Contains(OrgAudit.SchuljahrAbgeschlossen, audit);
        Assert.Contains(OrgAudit.SchuljahrWiedereroeffnet, audit);

        // Läuft ein anderes Jahr, kann ein geschlossenes nicht mehr geöffnet werden.
        await w.D.Jahre.JahrAbschliessenAsync(w.Direktor, w.Jahr);
        await w.D.Jahre.JahrStartenAsync(w.Direktor, w.JahrVorb);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrWiedereroeffnenAsync(w.Direktor, w.Jahr, "Encore"));
    }

    [Fact]
    public async Task Perioden_im_Jahr_ohne_Ueberlappung_Examen_blanc_erlaubt()
    {
        var w = await OrgWelt.ErstelleAsync();
        // Laufendes Jahr: keine neue zählende Periode (Abrechnung), aber ein Examen blanc innerhalb einer Periode.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.PeriodeHinzufuegenAsync(w.Direktor, w.Jahr,
            new PeriodeEingabe("Bonus", new(2026, 12, 19), new(2027, 1, 3), 1, true)));
        var blanc = await w.D.Jahre.PeriodeHinzufuegenAsync(w.Direktor, w.Jahr, new PeriodeEingabe("Examen blanc", new(2026, 12, 1), new(2026, 12, 5), 1, false));
        var periode = await w.MitDbAsync(db => db.Perioden.SingleAsync(p => p.Id == blanc));
        Assert.False(periode.ZaehltFuerJahr);
        Assert.Equal(0, periode.Jahresgewicht);
        Assert.Equal(4, periode.Nr);
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.PeriodeHinzufuegenAsync(w.Direktor, w.Jahr,
            new PeriodeEingabe("Examen blanc", new(2027, 2, 1), new(2027, 2, 5), 1, false)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.PeriodeHinzufuegenAsync(w.Direktor, w.Jahr,
            new PeriodeEingabe("Hors année", new(2027, 6, 20), new(2027, 7, 5), 1, false)));

        // In Vorbereitung: Datum außerhalb oder überlappend wird abgewiesen.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.PeriodeAendernAsync(w.Direktor, w.PV1,
            new PeriodeEingabe("1er trimestre", new(2027, 9, 1), new(2028, 1, 10), 1, true)));
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.PeriodeAendernAsync(w.Direktor, w.PV1,
            new PeriodeEingabe("1er trimestre", new(2027, 8, 20), new(2027, 12, 18), 1, true)));
        await w.D.Jahre.PeriodeAendernAsync(w.Direktor, w.PV1, new PeriodeEingabe("1er trimestre", new(2027, 9, 6), new(2027, 12, 20), 2, true));
        Assert.Equal(2, (await w.MitDbAsync(db => db.Perioden.SingleAsync(p => p.Id == w.PV1))).Jahresgewicht);

        // Struktur neu nur in Vorbereitung.
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.StrukturNeuAsync(w.Direktor, w.Jahr, PeriodenArt.Semestre, 2));
        await w.D.Jahre.StrukturNeuAsync(w.Direktor, w.JahrVorb, PeriodenArt.Session, 4);
        Assert.Equal(4, await w.MitDbAsync(db => db.Perioden.CountAsync(p => p.SchuljahrId == w.JahrVorb)));

        // Periode schließen/öffnen mit Audit.
        await w.D.Jahre.PeriodeStatusAsync(w.Koordinator, w.P1, true);
        Assert.Equal(PeriodenStatus.Geschlossen, (await w.MitDbAsync(db => db.Perioden.SingleAsync(p => p.Id == w.P1))).Status);
        Assert.Equal(1, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.Aktion == OrgAudit.PeriodeStatus)));
    }

    [Fact]
    public async Task Jahr_loeschen_nur_leer_und_in_Vorbereitung()
    {
        var w = await OrgWelt.ErstelleAsync();
        await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.JahrLoeschenAsync(w.Direktor, w.Jahr));
        await w.D.Jahre.JahrLoeschenAsync(w.Direktor, w.JahrVorb);
        Assert.False(await w.MitDbAsync(db => db.Schuljahre.AnyAsync(j => j.Id == w.JahrVorb)));
        Assert.False(await w.MitDbAsync(db => db.Perioden.AnyAsync(p => p.SchuljahrId == w.JahrVorb)));
    }
    [Fact]
    public async Task Daten_einer_abgerechneten_Periode_bleiben_fest()
    {
        // Fehlersuche 21.11.2026 (A4), Nutzerentscheidung 22.11.: nach der Abrechnung entstanden durch geänderte Daten Lücken
        // zwischen den Perioden (nie berechnete Tage). Titel und Koeffizient bleiben änderbar; eine annullierte Rechnung sperrt nicht.
        var w = await OrgWelt.ErstelleAsync();
        var rechnungId = Guid.NewGuid();
        await using (var db = await w.D.Db.CreateDbContextAsync())
        {
            db.Plattformrechnungen.Add(new Plattformrechnung
            {
                Id = rechnungId, Nummer = "SKL-2026-0001", SchuleId = w.SchuleA, SchuljahrId = w.Jahr, PeriodeId = w.P1,
                Datum = new(2026, 12, 19), Faellig = new(2027, 1, 18), Anzahl = 1, Betrag = 5_000, Status = RechnungsStatus.Ouverte
            });
            await db.SaveChangesAsync();
        }

        var fehler = await Assert.ThrowsAsync<FachFehler>(() => w.D.Jahre.PeriodeAendernAsync(w.Direktor, w.P1,
            new PeriodeEingabe("1er trimestre", new(2026, 9, 1), new(2026, 12, 11), 1, true)));
        Assert.Contains("déjà facturée", fehler.Message);
        await w.D.Jahre.PeriodeAendernAsync(w.Direktor, w.P1, new PeriodeEingabe("Trimestre 1", new(2026, 9, 1), new(2026, 12, 18), 2, true));
        Assert.Equal(("Trimestre 1", 2), await w.MitDbAsync(db => db.Perioden.Where(p => p.Id == w.P1).Select(p => ValueTuple.Create(p.Titel, p.Jahresgewicht)).SingleAsync()));

        await using (var db = await w.D.Db.CreateDbContextAsync())
        {
            (await db.Plattformrechnungen.SingleAsync(r => r.Id == rechnungId)).Status = RechnungsStatus.Annulee;
            await db.SaveChangesAsync();
        }
        await w.D.Jahre.PeriodeAendernAsync(w.Direktor, w.P1, new PeriodeEingabe("Trimestre 1", new(2026, 9, 1), new(2026, 12, 11), 2, true));
    }
}
