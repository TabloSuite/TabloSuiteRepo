using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

public class PlattformRechnungTests
{
    private static readonly DateOnly NachP1 = new(2026, 12, 19);

    private static async Task<Plattformrechnung> EinzigeAsync(FinanzWelt w)
        => await w.MitDbAsync(db => db.Plattformrechnungen.Include(r => r.Positionen).SingleAsync());

    [Fact]
    public async Task Rechnung_nach_Periodenende_genau_einmal()
    {
        var w = await FinanzWelt.ErstelleAsync();
        Assert.Equal(0, await w.Abrechnung.FaelligeAbrechnenAsync());
        w.Heute(NachP1);
        Assert.Equal(1, await w.Abrechnung.FaelligeAbrechnenAsync());
        Assert.Equal(0, await w.Abrechnung.FaelligeAbrechnenAsync());
        var r = await EinzigeAsync(w);
        Assert.Equal(("SKL-2026-0001", 2, 10_000L, RechnungsStatus.Ouverte), (r.Nummer, r.Anzahl, r.Betrag, r.Status));
        Assert.Equal((NachP1, NachP1.AddDays(30), w.Org.P1), (r.Datum, r.Faellig, r.PeriodeId));
        Assert.All(r.Positionen, p => Assert.Equal(5_000, p.Betrag));   // 4 Monate × 1.250
        Assert.Null(r.ErgaenztId);
        Assert.Equal("%PDF"u8.ToArray(), (await w.Rechnungen.PdfAsync(w.D, r.Id)).Inhalt[..4]);
    }

    [Fact]
    public async Task Perioden_vor_der_Anmeldung_der_Schule_werden_nie_berechnet()
    {
        // 21.11.2026: Einschreibungen beginnen standardmäßig am Schuljahresbeginn – eine Schule, die im Januar startet,
        // zahlt das 1er trimestre (schon vorbei) nicht rückwirkend; das 2e trimestre normal.
        var w = await FinanzWelt.ErstelleAsync();
        await w.MitDbAsync(async db =>
        {
            (await db.Schulen.SingleAsync(s => s.Id == w.Org.SchuleA)).ErstelltAmUtc = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            return await db.SaveChangesAsync();
        });
        w.Heute(new(2027, 3, 27));
        Assert.Equal(1, await w.Abrechnung.FaelligeAbrechnenAsync());
        Assert.Equal(w.Org.P2, (await EinzigeAsync(w)).PeriodeId);
        var fehler = await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1));
        Assert.Contains("avant l'inscription de l'établissement", fehler.Message);
        var liste = await w.Rechnungen.AbrechenbareAsync(w.Org.Admin, w.Org.SchuleA);
        Assert.Contains(liste, p => p.PeriodeId == w.Org.P1 && p.VorAnmeldung);
        Assert.Contains(liste, p => p.PeriodeId == w.Org.P2 && !p.VorAnmeldung);
    }

    [Fact]
    public async Task Anteil_ist_Monatspreis_mal_Monate_der_Periode()
    {
        // 19.11.2026: P1 01.09.–18.12. (109 Tage) = 4 Monate, P2 04.01.–26.03. (82 Tage) = 3 Monate; eingefroren 1.250 je Monat.
        var w = await FinanzWelt.ErstelleAsync();
        w.Heute(new(2027, 3, 27));
        Assert.Equal(2, await w.Abrechnung.FaelligeAbrechnenAsync());
        var rechnungen = await w.MitDbAsync(db => db.Plattformrechnungen.Include(r => r.Positionen).ToListAsync());
        var p1 = rechnungen.Single(r => r.PeriodeId == w.Org.P1);
        var p2 = rechnungen.Single(r => r.PeriodeId == w.Org.P2);
        Assert.All(p1.Positionen, p => Assert.Equal(4 * 1_250, p.Betrag));
        Assert.All(p2.Positionen, p => Assert.Equal(3 * 1_250, p.Betrag));
        Assert.Equal(p2.Positionen.Count * 3_750L, p2.Betrag);

        var detail = await w.Rechnungen.DetailAsync(w.D, p2.Id);
        Assert.Equal((3_750L, 3, 1_250L), (detail.ProSchueler, detail.Monate, detail.Monatspreis));
        Assert.Equal("(3 mois × 1.250 FCFA)", detail.Aufschluesselung);
        Assert.Contains(await w.MitDbAsync(db => db.AuditEintraege.Select(a => a.Text).ToListAsync()), t => t.Contains("(3 mois × 1.250 FCFA)"));

        // Passt der Betrag nicht zum eingefrorenen Monatspreis (z. B. Rechnung nach altem Jahrespreis), keine Aufschlüsselung.
        await w.MitDbAsync(async db =>
        {
            (await db.Schuljahre.SingleAsync(j => j.Id == w.Org.Jahr)).PlattformMonatspreis = 1_000;
            return await db.SaveChangesAsync();
        });
        var alt = await w.Rechnungen.DetailAsync(w.D, p2.Id);
        Assert.Null(alt.Monatspreis);
        Assert.Equal(string.Empty, alt.Aufschluesselung);
    }

    [Fact]
    public async Task Responsable_sieht_auf_der_Rechnung_keine_Schuelerliste()
    {
        // 18.11.2026: Formation und CGU versprechen, dass der Responsable keine Schülerdaten sieht – Rechnung nur mit Anzahl.
        var w = await FinanzWelt.ErstelleAsync();
        w.Heute(NachP1);
        await w.Abrechnung.FaelligeAbrechnenAsync();
        var r = await EinzigeAsync(w);
        var po = await w.Rechnungen.DetailAsync(w.Org.Responsable, r.Id);
        Assert.True(po.SchuelerVerborgen);
        Assert.Empty(po.Positionen);
        Assert.Equal((2, 10_000L), (po.Kopf.Anzahl, po.Kopf.Betrag));
        Assert.Equal("%PDF"u8.ToArray(), (await w.Rechnungen.PdfAsync(w.Org.Responsable, r.Id)).Inhalt[..4]);
        foreach (var mitListe in new[] { w.D, w.Org.Admin })
        {
            var d = await w.Rechnungen.DetailAsync(mitListe, r.Id);
            Assert.False(d.SchuelerVerborgen);
            Assert.Equal(2, d.Positionen.Count);
        }
    }

    [Fact]
    public async Task Klassenwechsel_einmal_und_Nachtrag_fuer_rueckwirkende_Einschreibung()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.Org.D.Einschreibung.KlasseWechselnAsync(w.D, w.Org.SchuelerZiel, w.Org.Klasse6B, new(2026, 11, 2));
        w.Heute(NachP1);
        await w.Abrechnung.FaelligeAbrechnenAsync();
        var haupt = await EinzigeAsync(w);
        Assert.Equal(2, haupt.Anzahl);

        // Später, rückwirkend eingeschrieben → Nachtragsrechnung mit Verweis auf die erste.
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            db.Einschreibungen.Add(new Einschreibung { SchuleId = w.Org.SchuleA, SchuelerId = w.Org.SchuelerOhne, SchuljahrId = w.Org.Jahr, KlasseId = w.Org.Klasse5A, Von = new(2026, 10, 1), Status = EinschreibungsStatus.Active });
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await w.Abrechnung.FaelligeAbrechnenAsync());
        var nachtrag = await w.MitDbAsync(db => db.Plattformrechnungen.SingleAsync(r => r.Id != haupt.Id));
        Assert.Equal(("SKL-2026-0002", 1, haupt.Id), (nachtrag.Nummer, nachtrag.Anzahl, nachtrag.ErgaenztId));
        Assert.True((await w.Rechnungen.DetailAsync(w.D, nachtrag.Id)).Kopf.IstNachtrag);
    }

    [Fact]
    public async Task Melden_bestaetigen_Anteil_und_Storno_mit_Ausgleich()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.Tarife.EinstellungSpeichernAsync(w.Org.Admin, new TarifEinstellung(1_250, 30, 10));
        w.Heute(NachP1);
        await w.Abrechnung.FaelligeAbrechnenAsync();
        var r = await EinzigeAsync(w);

        await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.MeldenAsync(w.D, r.Id, new MeldungEingabe(10_000, NachP1, ""), null));
        await w.Rechnungen.MeldenAsync(w.KMit, r.Id, new MeldungEingabe(10_000, NachP1, "VIR-001"), new BelegDatei(new MemoryStream("%PDF-1.7"u8.ToArray()), "avis.pdf"));
        await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.MeldenAsync(w.D, r.Id, new MeldungEingabe(10_000, NachP1, "VIR-002"), null));
        var detail = await w.Rechnungen.DetailAsync(w.Org.Admin, r.Id);
        Assert.Equal(RechnungsStatus.PaiementDeclare, detail.Kopf.Status);
        var meldung = Assert.Single(detail.Meldungen);
        var ausgabe = await w.MitDbAsync(db => db.Buchungen.SingleAsync(b => b.Id == meldung.BuchungId));
        Assert.Equal((BuchungsHerkunft.FactureSkoulou, BuchungsRichtung.Depense, 10_000L), (ausgabe.Herkunft, ausgabe.Richtung, ausgabe.Betrag));
        await w.Journal.BelegAsync(w.Org.Admin, ausgabe.Id);

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Rechnungen.BestaetigenAsync(w.Org.Responsable, r.Id));
        await w.Rechnungen.BestaetigenAsync(w.Org.Admin, r.Id);
        await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.BestaetigenAsync(w.Org.Admin, r.Id));
        Assert.Equal(new AnteilSummen(1_000, 1, 0), await w.Anteile.SummenAsync(w.Org.Responsable, null));

        await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.StornierenAsync(w.Org.Admin, r.Id, " "));
        await w.Rechnungen.StornierenAsync(w.Org.Admin, r.Id, "Doublon d'élèves de test");
        var anteile = await w.MitDbAsync(db => db.ResponsableAnteile.OrderBy(a => a.Betrag).ToListAsync());
        Assert.Equal([-1_000L, 1_000], anteile.Select(a => a.Betrag));
        Assert.Equal(anteile[1].Id, anteile[0].AusgleichFuerId);
        Assert.Equal(0, (await w.Anteile.SummenAsync(w.Org.Admin, w.Org.Responsable)).Offen);
        Assert.All(await w.MitDbAsync(db => db.Rechnungspositionen.ToListAsync()), p => Assert.NotNull(p.StorniertGrund));

        // Automatisch wird nicht erneut berechnet, « Facturer maintenant » schon.
        Assert.Equal(0, await w.Abrechnung.FaelligeAbrechnenAsync());
        Assert.NotNull(await w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1));
        Assert.Null(await w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1));
        Assert.Equal(2, (await w.Rechnungen.ZaehlerAsync(w.Org.Admin)).Alle);
    }

    [Fact]
    public async Task Anfechten_beantworten_und_vorzeitig_abrechnen()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var periode = (await w.Rechnungen.AbrechenbareAsync(w.Org.Admin, w.Org.SchuleA)).First(p => p.PeriodeId == w.Org.P1);
        Assert.Equal((2, 5_000L, false), (periode.Abrechenbar, periode.ProSchueler, periode.Beendet));
        var id = (await w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1))!.Value;

        await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.AnfechtenAsync(w.D, id, ""));
        await w.Rechnungen.AnfechtenAsync(w.D, id, "Deux élèves sont partis en septembre.");
        await Assert.ThrowsAsync<FachFehler>(() => w.Rechnungen.MeldenAsync(w.D, id, new MeldungEingabe(1, new(2026, 11, 13), "x"), null));
        Assert.Single((await w.Rechnungen.ListeAsync(w.D, RechnungService.FilterContestees, null, null, 0, 50)).Eintraege);
        await w.Rechnungen.AufrechterhaltenAsync(w.Org.Admin, id, "Les deux élèves étaient inscrits pendant la période.");
        var detail = await w.Rechnungen.DetailAsync(w.D, id);
        Assert.Equal(RechnungsStatus.Ouverte, detail.Kopf.Status);
        Assert.StartsWith("Contestation examinée", detail.Kopf.Grund);
        Assert.Equal(2, detail.Positionen.Count);
        Assert.Equal(3, await w.MitDbAsync(db => db.AuditEintraege.CountAsync(a => a.ZielId == id)));
    }

    [Fact]
    public async Task Erinnerungen_bei_Faelligkeit_und_sieben_Tage_spaeter()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await using (var db = await w.Db.CreateDbContextAsync())
        {
            (await db.Users.FirstAsync(k => k.Id == w.D)).Email = "direction@exemple.tg";
            await db.SaveChangesAsync();
        }
        w.Heute(NachP1);
        await w.Abrechnung.FaelligeAbrechnenAsync();
        Assert.Equal(1, await w.MitDbAsync(db => db.MailAusgaenge.CountAsync(m => m.Bezug == "facture-skoulou")));
        Assert.Equal(0, await w.Abrechnung.ErinnernAsync());
        w.Heute(NachP1.AddDays(30));
        Assert.Equal(1, await w.Abrechnung.ErinnernAsync());
        Assert.Equal(0, await w.Abrechnung.ErinnernAsync());
        w.Heute(NachP1.AddDays(37));
        Assert.Equal(1, await w.Abrechnung.ErinnernAsync());
        w.Heute(NachP1.AddDays(60));
        Assert.Equal(0, await w.Abrechnung.ErinnernAsync());
        Assert.Equal(2, await w.MitDbAsync(db => db.MailAusgaenge.CountAsync(m => m.Bezug == "facture-skoulou-rappel")));
        Assert.Equal(2, (await EinzigeAsync(w)).Erinnerungen);
        Assert.Equal(1, (await w.Rechnungen.ZaehlerAsync(w.D)).EnRetard);
    }

    [Fact]
    public async Task Auszahlung_der_Anteile_und_Tarife()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.Tarife.SchulTarifSpeichernAsync(w.Org.Admin, w.Org.SchuleA, 2_000, 12.5m);
        await Assert.ThrowsAsync<FachFehler>(() => w.Tarife.SchulTarifSpeichernAsync(w.Org.Admin, w.Org.SchuleA, 0, null));
        await Assert.ThrowsAsync<FachFehler>(() => w.Tarife.EinstellungSpeichernAsync(w.Org.Admin, new TarifEinstellung(1_250, 400, 0)));
        Assert.Contains((await w.Tarife.SchulenAsync(w.Org.Admin, "Alpha", 0, 50)).Eintraege, s => s.Preis == 2_000 && s.Eingefroren == 1_250);

        w.Heute(NachP1);
        await w.Abrechnung.FaelligeAbrechnenAsync();
        var r = await EinzigeAsync(w);
        Assert.Equal(10_000, r.Betrag); // eingefrorener Monatspreis 1.250 × 4 Monate, nicht der neue Schulpreis
        await w.Rechnungen.BestaetigenAsync(w.Org.Admin, r.Id);
        var anteil = Assert.Single((await w.Anteile.ListeAsync(w.Org.Responsable, null, AnteilService.FilterOffen, 0, 50)).Eintraege);
        Assert.Equal((1_250L, 12.5m), (anteil.Betrag, anteil.Prozent));

        await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Anteile.AuszahlenAsync(w.Org.Responsable, [anteil.Id], NachP1, "OM-1"));
        await Assert.ThrowsAsync<FachFehler>(() => w.Anteile.AuszahlenAsync(w.Org.Admin, [anteil.Id], NachP1, ""));
        Assert.Equal(1, await w.Anteile.AuszahlenAsync(w.Org.Admin, [anteil.Id], NachP1, "OM-1"));
        await Assert.ThrowsAsync<FachFehler>(() => w.Anteile.AuszahlenAsync(w.Org.Admin, [anteil.Id], NachP1, "OM-2"));
        Assert.Equal(new AnteilSummen(0, 0, 1_250), await w.Anteile.SummenAsync(w.Org.Responsable, null));
        Assert.Contains(await w.Anteile.ResponsablesAsync(w.Org.Admin), x => x.Id == w.Org.Responsable && x.Offen == 0);
    }
}
