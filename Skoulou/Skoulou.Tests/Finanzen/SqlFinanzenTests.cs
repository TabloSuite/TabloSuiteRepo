using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Tests.Organisation;
using Skoulou.Tests.Sql;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

/// <summary>
/// Nur mit SKOULOU_SQL_TESTS=1: eindeutige Indizes der Finanzen, Doppelklick und parallele Zahlungen (Nummernkreis per MERGE),
/// Plattformrechnung und Übersetzbarkeit der Listen auf SQL Server.
/// </summary>
public class SqlFinanzenTests(SqlDatenbank datenbank) : IClassFixture<SqlDatenbank>
{
    private Task<FinanzWelt> WeltAsync() => FinanzWelt.ErstelleAsync(new SqlFabrik(datenbank), new TestZeit());

    [SqlFact]
    public async Task Eindeutige_Indizes_der_Finanzen()
    {
        var w = await WeltAsync();
        var z = await w.ZahlenAsync(w.Org.Schueler, 10_000);
        var id = (await w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1))!.Value;
        var position = await w.MitDbAsync(db => db.Rechnungspositionen.FirstAsync(p => p.RechnungId == id));

        // Ein Schüler höchstens einmal je Periode (nicht stornierte Positionen).
        await using (var db = datenbank.Kontext())
        {
            db.Rechnungspositionen.Add(new Rechnungsposition { RechnungId = id, PeriodeId = w.Org.P1, SchuelerId = position.SchuelerId, EinschreibungId = position.EinschreibungId, Betrag = 1 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = datenbank.Kontext())
        {
            db.Rechnungspositionen.Add(new Rechnungsposition { RechnungId = id, PeriodeId = w.Org.P1, SchuelerId = position.SchuelerId, EinschreibungId = position.EinschreibungId, Betrag = 1, StorniertGrund = "Essai" });
            await db.SaveChangesAsync();
        }
        // Quittungsnummer je Schule eindeutig.
        await using (var db = datenbank.Kontext())
        {
            db.Zahlungen.Add(new Zahlung { SchuleId = w.Org.SchuleA, SchuelerId = w.Org.Schueler, SchuljahrId = w.Org.Jahr, Betrag = 1, Datum = new(2026, 11, 1), Quittungsnummer = z.Quittungsnummer, ErfasstVonId = w.D });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        // Jede Buchung höchstens einmal aufgehoben.
        await w.Zahlungen.StornierenAsync(w.D, z.ZahlungId, "Essai");
        await using (var db = datenbank.Kontext())
        {
            var gegen = await db.Buchungen.AsNoTracking().SingleAsync(b => b.Herkunft == BuchungsHerkunft.AnnulationPaiement && b.HerkunftId == z.ZahlungId);
            db.Buchungen.Add(new Buchung { SchuleId = gegen.SchuleId, Datum = gegen.Datum, Richtung = gegen.Richtung, KategorieId = gegen.KategorieId, Betrag = 1, Text = "x", Nummer = "ECR-TEST-1", Herkunft = BuchungsHerkunft.Annulation, StornoVonId = gegen.StornoVonId, ErfasstVonId = w.D });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [SqlFact]
    public async Task Doppelklick_und_parallele_Zahlungen()
    {
        var w = await WeltAsync();
        // Fünfmal derselbe Vorgang gleichzeitig → genau eine Zahlung, überall dieselbe Quittung.
        var eingabe = new ZahlungEingabe(Guid.NewGuid(), w.Org.Schueler, 5_000, new(2026, 11, 12), ZahlungsKanal.Especes, null, null);
        var gleiche = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => w.Zahlungen.ErfassenAsync(w.D, w.Org.Jahr, eingabe))));
        Assert.Single(gleiche.Select(g => g.Quittungsnummer).Distinct());
        Assert.Equal(1, await w.MitDbAsync(db => db.Zahlungen.CountAsync(z => z.Id == eingabe.VorgangId)));

        // Verschiedene Zahlungen gleichzeitig → lückenlose, eindeutige Nummern und je eine Buchung.
        var viele = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => Task.Run(() =>
            w.ZahlenAsync(i % 2 == 0 ? w.Org.Schueler : w.Org.SchuelerZiel, 1_000 + i))));
        var nummern = viele.Select(v => v.Quittungsnummer).Order().ToList();
        Assert.Equal(Enumerable.Range(2, 6).Select(n => $"REC-2026-{n:00000}"), nummern);
        Assert.Equal(7, await w.MitDbAsync(db => db.Buchungen.CountAsync(b => b.SchuleId == w.Org.SchuleA && b.Herkunft == BuchungsHerkunft.Paiement)));
        Assert.Equal(7, await w.MitDbAsync(db => db.Buchungen.Where(b => b.SchuleId == w.Org.SchuleA).Select(b => b.Nummer).Distinct().CountAsync()));
    }

    [SqlFact]
    public async Task Dienste_und_Listen_auf_SQL_Server()
    {
        var w = await WeltAsync();
        var j = w.Org.Jahr;
        await w.ZahlenAsync(w.Org.Schueler, 30_000, ZahlungsKanal.MobileMoney);
        await w.Gebuehren.ErmaessigungAnlegenAsync(w.D, j, new ErmaessigungEingabe(w.Org.SchuelerZiel, w.Scolarite, ErmaessigungsArt.Pourcentage, 25, "Fratrie"));
        var loyer = (await w.Journal.KategorienAsync(w.D)).Single(k => k.Name == "Loyer").Id;
        await w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 2), loyer, 20_000, "Loyer"), new BelegDatei(new MemoryStream("%PDF-1.4"u8.ToArray()), "q.pdf"));
        await w.Gebuehren.VorjahrKopierenAsync(w.D, w.Org.JahrVorb);

        foreach (var filter in new string?[] { null, GebuehrenService.FilterEnRetard, GebuehrenService.FilterSoldes, GebuehrenService.FilterAvoirs, GebuehrenService.FilterReste })
            await w.Gebuehren.KontenAsync(w.D, j, w.Org.Klasse6A, filter, "eleve", 0, 50);
        Assert.Equal(2, (await w.Gebuehren.ZaehlerAsync(w.D, j, null)).Alle);
        Assert.Equal(55_000, (await w.Gebuehren.KontoAsync(w.D, j, w.Org.SchuelerZiel)).Konto.Soll);
        foreach (var filter in new string?[] { null, ZahlungService.FilterEspeces, ZahlungService.FilterMobile, ZahlungService.FilterBanque, ZahlungService.FilterAnnules })
            await w.Zahlungen.SucheAsync(w.D, j, filter, "REC-2026_%[", 0, 50);
        Assert.Single((await w.Zahlungen.SucheAsync(w.D, j, null, "eleve", 0, 50)).Eintraege);
        await w.Zahlungen.ZaehlerAsync(w.D, j);
        Assert.NotEmpty(await w.Zahlungen.SchuelerSuchenAsync(w.D, j, "eleve"));
        var journal = await w.Journal.SucheAsync(w.D, new JournalFilter(new(2026, 9, 1), new(2026, 12, 31), null, null, null), 0, 50);
        Assert.Equal(10_000, journal.Eintraege[0].SaldoNach);
        await w.Journal.SucheAsync(w.D, new JournalFilter(null, null, BuchungsRichtung.Depense, loyer, "loy"), 0, 50);
        await w.Journal.SummenAsync(w.D, new JournalFilter(null, null, null, null, null));
        await w.Journal.KasseAsync(w.D, j);
        await w.Bericht.BerichtAsync(w.D, new(2026, 9, 1), new(2026, 11, 13));
        await w.Quittungen.PdfAsync(w.Org.Eltern, (await w.Zahlungen.SucheAsync(w.D, j, null, null, 0, 1)).Eintraege[0].Id);

        // Plattformrechnung, Meldung, Bestätigung, Anteil, Auszahlung, Tarife.
        await w.Tarife.EinstellungSpeichernAsync(w.Org.Admin, new TarifEinstellung(1_250, 30, 10));
        w.Heute(new(2026, 12, 19));
        Assert.True(await w.Abrechnung.FaelligeAbrechnenAsync() >= 1); // andere Testschulen derselben DB zählen mit
        Assert.Equal(0, await w.Abrechnung.FaelligeAbrechnenAsync());
        var rechnung = (await w.Rechnungen.ListeAsync(w.D, null, null, null, 0, 50)).Eintraege.Single();
        await w.Rechnungen.MeldenAsync(w.D, rechnung.Id, new MeldungEingabe(rechnung.Betrag, new(2026, 12, 19), "VIR-1"), null);
        await w.Rechnungen.BestaetigenAsync(w.Org.Admin, rechnung.Id);
        foreach (var filter in new string?[] { null, RechnungService.FilterOuvertes, RechnungService.FilterDeclarees, RechnungService.FilterContestees, RechnungService.FilterPayees, RechnungService.FilterAnnulees, RechnungService.FilterRetard })
            await w.Rechnungen.ListeAsync(w.Org.Admin, filter, w.Org.SchuleA, "SKL", 0, 50);
        await w.Rechnungen.ZaehlerAsync(w.Org.Responsable);
        await w.Rechnungen.DetailAsync(w.Org.Responsable, rechnung.Id);
        await w.Rechnungen.AbrechenbareAsync(w.Org.Admin, w.Org.SchuleA);
        await w.Rechnungen.SchulenAsync(w.Org.Admin);
        var anteile = await w.Anteile.ListeAsync(w.Org.Responsable, null, AnteilService.FilterOffen, 0, 50);
        Assert.Equal(1_000, anteile.Eintraege.Single().Betrag);   // 10 % von 2 × (4 Monate × 1.250)
        await w.Anteile.ResponsablesAsync(w.Org.Admin);
        Assert.Equal(1, await w.Anteile.AuszahlenAsync(w.Org.Admin, [anteile.Eintraege[0].Id], new(2026, 12, 19), "OM-1"));
        await w.Tarife.SchulenAsync(w.Org.Admin, "Alpha", 0, 50);
        await w.Rechnungen.StornierenAsync(w.Org.Admin, rechnung.Id, "Essai");
        Assert.Equal(-1_000, (await w.Anteile.SummenAsync(w.Org.Responsable, null)).Offen);
        await w.Familie.KontoAsync(w.Org.Eltern, w.Org.Schueler);
    }
}
