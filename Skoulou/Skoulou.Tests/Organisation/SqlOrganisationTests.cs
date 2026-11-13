using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Import;

namespace Skoulou.Tests.Organisation;

/// <summary>DbContext-Fabrik über der SQL-Testdatenbank (mit Wiederholungsstrategie wie in der App).</summary>
internal sealed class SqlFabrik(SqlDatenbank datenbank) : IDbContextFactory<SkoulouDbContext>
{
    public SkoulouDbContext CreateDbContext() => datenbank.Kontext(mitWiederholung: true);
}

/// <summary>
/// Nur mit SKOULOU_SQL_TESTS=1: Unique-Indizes, auf die sich der Bereich Organisation verlässt (soweit nicht schon in
/// <see cref="SqlServerTests"/> geprüft), und Übersetzbarkeit der Listen- und Detailabfragen auf SQL Server.
/// </summary>
public class SqlOrganisationTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private Task<OrgWelt> WeltAsync() => OrgWelt.ErstelleAsync(new SqlFabrik(datenbank), new TestZeit());

    private async Task VerletztAsync(Action<SkoulouDbContext> aenderung)
    {
        await using var db = datenbank.Kontext();
        aenderung(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlFact]
    public async Task Eindeutige_Indizes_der_Organisation()
    {
        var w = await WeltAsync();
        var a = w.SchuleA;
        // Matrikel je Schule (auch archivierte); in einer anderen Schule erlaubt.
        await VerletztAsync(db => db.SchuelerProfile.Add(new SchuelerProfil { KontoId = w.Lehrer2, SchuleId = a, Matrikel = "CA260001" }));
        await using (var db = datenbank.Kontext())
        {
            db.SchuelerProfile.Add(new SchuelerProfil { KontoId = w.Lehrer2, SchuleId = w.SchuleB, Matrikel = "CA260001" });
            await db.SaveChangesAsync();
        }
        // Profil je Konto genau einmal; mehrere Profile ohne Matrikel/Kartencode erlaubt (gefilterte Indizes).
        await VerletztAsync(db => db.SchuelerProfile.Add(new SchuelerProfil { KontoId = w.Schueler, SchuleId = a }));
        await using (var db = datenbank.Kontext())
        {
            db.SchuelerProfile.Add(new SchuelerProfil { KontoId = w.LehrerZiel, SchuleId = a });
            db.SchuelerProfile.Add(new SchuelerProfil { KontoId = w.KoordZiel, SchuleId = a });
            await db.SaveChangesAsync();
        }
        // Kartencode plattformweit eindeutig.
        await using (var db = datenbank.Kontext())
        {
            db.SchuelerProfile.Single(p => p.KontoId == w.Schueler).KartenToken = "abc" + w.Kennung;
            await db.SaveChangesAsync();
            db.SchuelerProfile.Single(p => p.KontoId == w.SchuelerZiel).KartenToken = "abc" + w.Kennung;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await VerletztAsync(db => db.Stufen.Add(new Stufe { SchuleId = a, Name = "6e" }));
        await VerletztAsync(db => db.Faecher.Add(new Fach { SchuleId = a, Name = "Mathématiques" }));
        await VerletztAsync(db => db.Klassenfaecher.Add(new Klassenfach { SchuleId = a, KlasseId = w.Klasse6A, FachId = w.FachMath }));
        await VerletztAsync(db => db.Lehrauftraege.Add(new Lehrauftrag { SchuleId = a, KlassenfachId = w.Kf6AMath, LehrerId = w.Lehrer, Wochenstunden = 1 }));
        await VerletztAsync(db => db.ElternKinder.Add(new ElternKind { SchuleId = a, ElternId = w.Eltern, SchuelerId = w.Schueler }));
        await VerletztAsync(db => db.Perioden.Add(new Periode { SchuleId = a, SchuljahrId = w.Jahr, Nr = 1, Titel = "Doublon", Beginn = new(2026, 9, 1), Ende = new(2026, 9, 2) }));
        await using (var db = datenbank.Kontext())
        {
            db.Anwesenheiten.Add(new Anwesenheit { SchuleId = a, EinschreibungId = w.EinschreibungSchueler, Datum = OrgWelt.Heute, ErfasstVonId = w.Direktor });
            await db.SaveChangesAsync();
            db.Anwesenheiten.Add(new Anwesenheit { SchuleId = a, EinschreibungId = w.EinschreibungSchueler, Datum = OrgWelt.Heute, ErfasstVonId = w.Lehrer });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // Eine aktive Einschreibung je Schüler; geschlossene daneben erlaubt.
        await using (var db = datenbank.Kontext())
        {
            db.Einschreibungen.Add(new Einschreibung { SchuleId = a, SchuelerId = w.Schueler, SchuljahrId = w.Jahr, KlasseId = w.Klasse6B, Von = new(2026, 9, 1), Bis = new(2026, 9, 5), Status = EinschreibungsStatus.Changee });
            await db.SaveChangesAsync();
        }
    }

    [SqlFact]
    public async Task Dienste_arbeiten_auf_SQL_Server()
    {
        var w = await WeltAsync();
        var d = w.D;
        // Schreibvorgänge mit Transaktion, Nummernkreis (MERGE) und Unique-Indizes.
        var neu = await d.Schueler.AnlegenAsync(w.Direktor, OrgWelt.SchuelerDaten("Afi", "Koffi" + w.Kennung), w.Klasse6B);
        Assert.Equal("CA260004", (await d.Schueler.DetailAsync(w.Direktor, neu.Zugaenge[0].KontoId)).Daten.Matrikel);
        await d.Einschreibung.KlasseWechselnAsync(w.Direktor, w.SchuelerZiel, w.Klasse6B, new(2026, 11, 2));
        await d.Schueler.ValidierenAsync(w.Direktor, w.SchuelerWartend, w.Klasse6A, false);
        await d.Anwesenheit.AppellSpeichernAsync(w.Lehrer, w.Klasse6A, OrgWelt.Heute,
            [new AppellEingabe(w.EinschreibungSchueler, AnwesenheitsStatus.Retard, 10, false, "Bus")]);
        await d.Stundenplan.StundeSpeichernAsync(w.Direktor, w.PlanEntwurf, null, new StundeEingabe(w.Auftrag6B, DayOfWeek.Tuesday, new(8, 0), new(9, 0), "Salle 2"));
        await d.Stundenplan.VeroeffentlichenAsync(w.Direktor, w.PlanEntwurf);
        var jahrNeu = await d.Jahre.JahrAnlegenAsync(w.Direktor, new SchuljahrEingabe("2028-2029", new(2028, 9, 1), new(2029, 6, 30), PeriodenArt.Trimestre, 3));
        await d.Klassen.KlasseLoeschenAsync(w.Direktor, w.Klasse5A);
        await d.Konten.PasswortZuruecksetzenAsync(w.Direktor, [w.Schueler, w.Eltern]);
        await d.Eltern.AnlegenAsync(w.Direktor, new ElternEingabe("Kodzo", "Adjo" + w.Kennung, null, null, null, Geschlecht.Maennlich), w.SchuelerOhne, "Père");

        // Listen und Abfragen sind übersetzbar.
        foreach (var filter in new string?[] { null, SchuelerService.FilterAValider, SchuelerService.FilterSansClasse, SchuelerService.FilterBloques, SchuelerService.FilterArchives })
        {
            await d.Schueler.SucheAsync(w.Direktor, filter, null, null, 0, 50);
            await d.Schueler.SucheAsync(w.Direktor, filter, w.Klasse6A, "CA26_0%[", 0, 50);
        }
        Assert.Equal(2, (await d.Schueler.SucheAsync(w.Direktor, null, null, "CA26000", 0, 2)).Eintraege.Count);
        await d.Schueler.ZaehlerAsync(w.Direktor);
        await d.Schueler.AuswahlAsync(w.Direktor, "test");
        await d.Schueler.DetailAsync(w.Direktor, w.SchuelerZiel);
        foreach (var filter in new string?[] { null, PersonalService.FilterEnseignants, PersonalService.FilterCoordination, PersonalService.FilterSansAffectation, PersonalService.FilterBloques, PersonalService.FilterArchives })
            await d.Personal.SucheAsync(w.Direktor, w.Jahr, filter, "prof", 0, 50);
        Assert.Equal(1, (await d.Personal.ZaehlerAsync(w.Direktor, w.Jahr)).SansAffectation);
        var personal = await d.Personal.SucheAsync(w.Direktor, w.Jahr, null, null, 0, 50);
        Assert.Equal(3m, personal.Eintraege.Single(p => p.KontoId == w.Lehrer).Wochenstunden);
        foreach (var filter in new string?[] { null, ElternService.FilterOhneKind, ElternService.FilterBloques, ElternService.FilterArchives })
            await d.Eltern.SucheAsync(w.Direktor, filter, "CA260001", 0, 50);
        Assert.Single((await d.Eltern.SucheAsync(w.Direktor, null, "CA260001", 0, 50)).Eintraege);
        await d.Eltern.ZaehlerAsync(w.Direktor);
        await d.Eltern.AuswahlAsync(w.Direktor, "parent");
        await d.Eltern.DetailAsync(w.Direktor, w.Eltern);
        await d.Eltern.VorbelegungAsync(w.Direktor, w.Schueler);

        Assert.Equal(3, (await d.Jahre.JahreAsync(w.Direktor)).Count);
        await d.Jahre.VorschlagAsync(w.Direktor);
        await d.Jahre.JahrLoeschenAsync(w.Direktor, jahrNeu);
        var klassen = await d.Klassen.KlassenAsync(w.Direktor, w.Jahr);
        Assert.Equal(2, klassen.Single(k => k.Id == w.Klasse6A).Effectif);
        await d.Klassen.KlassenAuswahlAsync(w.Direktor, w.Jahr);
        await d.Klassen.StufenAsync(w.Direktor);
        await d.Klassen.LehrkraefteAsync(w.Direktor);
        await d.Faecher.FaecherAsync(w.Direktor);
        await d.Unterricht.KlassenfaecherAsync(w.Direktor, w.Klasse6A);
        await d.Unterricht.AuftraegeDesLehrersAsync(w.Direktor, w.Lehrer, w.Jahr);
        await d.Unterricht.KlassenfaecherDesJahresAsync(w.Direktor, w.Jahr);
        await d.Unterricht.KoeffizientenKopierenAsync(w.Direktor, w.Klasse6A, w.Klasse6B);

        await d.Stundenplan.PlaeneAsync(w.Direktor, w.Klasse6A);
        Assert.Single((await d.Stundenplan.PlanAsync(w.Direktor, w.PlanPub)).Stunden);
        Assert.Single((await d.Stundenplan.LehrerWocheAsync(w.Lehrer, OrgWelt.Heute)).Stunden);
        Assert.Single((await d.Stundenplan.SchuelerWocheAsync(w.Eltern, w.Schueler, OrgWelt.Heute)).Stunden);
        await d.Stundenplan.PlanPdfAsync(w.Schueler, w.PlanPub);
        await d.Stundenplan.LehrerPdfAsync(w.Lehrer, OrgWelt.Heute);
        await d.Stundenplan.KopierenAsync(w.Direktor, await d.Stundenplan.PlanAnlegenAsync(w.Direktor, w.Klasse6B, new(2027, 1, 4), null, null), w.PlanPub);

        Assert.Equal(2, (await d.Anwesenheit.AppellAsync(w.Direktor, w.Klasse6A, OrgWelt.Heute)).Zeilen.Count);
        await d.Anwesenheit.KlassenAsync(w.Lehrer, w.Jahr);
        Assert.Equal(1, (await d.Anwesenheit.KlassenUebersichtAsync(w.Direktor, w.Klasse6A, new(2026, 9, 1), OrgWelt.Heute)).TageMitAppell);
        Assert.Equal(1, (await d.Anwesenheit.SchuelerAsync(w.Eltern, w.Schueler)).Summe.Retards);
        var cockpit = await d.Cockpit.LadeAsync(w.Direktor);
        Assert.Equal(1, cockpit.RetardsHeute);
        Assert.Equal(0, cockpit.AValider);

        using var mappe = new XLWorkbook(new MemoryStream(await d.Import.VorlageAsync(w.Direktor, ImportArt.Eleves, w.Jahr)));
        var blatt = mappe.Worksheets.First();
        blatt.Cell(2, 1).Value = "Ama";
        blatt.Cell(2, 2).Value = "Mensah" + w.Kennung;
        blatt.Cell(2, 3).Value = "6e A";
        blatt.Cell(3, 1).Value = "Elom";
        blatt.Cell(3, 2).Value = "Gbeassor";
        blatt.Cell(3, 8).Value = "CA260001";
        var datei = new MemoryStream();
        mappe.SaveAs(datei);
        datei.Position = 0;
        var vorschau = await d.Import.VorschauAsync(w.Direktor, ImportArt.Eleves, w.Jahr, datei, "eleves.xlsx");
        Assert.Equal(1, vorschau.Gueltig);
        Assert.Single(vorschau.Fehler);
        Assert.Equal(1, (await d.Import.ImportierenAsync(w.Direktor, vorschau.Id)).Angelegt);
    }
}
