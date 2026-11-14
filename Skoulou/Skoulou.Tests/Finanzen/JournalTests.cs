using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

public class JournalTests
{
    private static readonly byte[] Pdf = "%PDF-1.4 justificatif"u8.ToArray();

    private static async Task<Guid> KategorieAsync(FinanzWelt w, string name)
        => (await w.Journal.KategorienAsync(w.D)).Single(k => k.Name == name).Id;

    [Fact]
    public async Task Manuelle_Buchung_mit_Beleg_und_Nummer()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var kategorien = await w.Journal.KategorienAsync(w.D);
        Assert.Contains(kategorien, k => k.Name == "Loyer" && !k.IstSystem);
        var scolarite = kategorien.Single(k => k.Name == "Scolarité");
        Assert.True(scolarite.IstSystem);

        var loyer = await KategorieAsync(w, "Loyer");
        var id = await w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 2), loyer, 20_000, "Loyer de novembre"), new BelegDatei(new MemoryStream(Pdf), "quittance.pdf"));
        var b = await w.MitDbAsync(db => db.Buchungen.SingleAsync(x => x.Id == id));
        Assert.Equal(("ECR-2026-00001", BuchungsRichtung.Depense, BuchungsHerkunft.Manuelle), (b.Nummer, b.Richtung, b.Herkunft));
        Assert.Single(w.Org.D.Speicher.Dateien);
        var beleg = await w.Journal.BelegAsync(w.D, id);
        Assert.Equal(("application/pdf", "quittance.pdf"), (beleg.ContentType, beleg.Dateiname));

        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 2), scolarite.Id, 1_000, "Essai"), null));
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 2), loyer, 1_000, "Essai"),
            new BelegDatei(new MemoryStream("MZ\x90\0"u8.ToArray()), "virus.pdf")));
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 2), loyer, 1_000, "Essai"),
            new BelegDatei(new MemoryStream([.. Pdf, .. new byte[BelegSpeicher.MaxBytes]]), "gros.pdf")));
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 12, 2), loyer, 1_000, "Futur"), null));
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 2), loyer, 1_000, " "), null));
        Assert.Single(w.Org.D.Speicher.Dateien);

        // Belege: Finanzpersonal, Responsable und Admin lesen; Lehrer, Koordinator ohne Recht und fremde Schule nicht.
        foreach (var erlaubt in new[] { w.KMit, w.Org.Responsable, w.Org.Admin })
            await w.Journal.BelegAsync(erlaubt, id);
        foreach (var verboten in new[] { w.KOhne, w.Org.Lehrer, w.Org.DirektorB, w.Org.Eltern })
            await Assert.ThrowsAsync<KeinZugriffFehler>(() => w.Journal.BelegAsync(verboten, id));
    }

    [Fact]
    public async Task Korrektur_nur_als_Gegenbuchung()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var don = await KategorieAsync(w, "Dons et subventions");
        var id = await w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 3), don, 5_000, "Don de l'APE"), null);
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.GegenbuchungAsync(w.D, id, ""));
        await w.Journal.GegenbuchungAsync(w.D, id, "Saisi deux fois");
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.GegenbuchungAsync(w.D, id, "Encore"));
        var gegen = await w.MitDbAsync(db => db.Buchungen.SingleAsync(b => b.StornoVonId == id));
        Assert.Equal((BuchungsRichtung.Depense, 5_000L, don), (gegen.Richtung, gegen.Betrag, gegen.KategorieId));
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.GegenbuchungAsync(w.D, gegen.Id, "Retour"));
        Assert.Equal(2, await w.MitDbAsync(db => db.Buchungen.CountAsync()));
        Assert.Equal(0, (await w.Journal.KasseAsync(w.D, w.Org.Jahr)).Saldo);
        var zeilen = (await w.Journal.SucheAsync(w.D, new JournalFilter(null, null, null, null, null), 0, 50)).Eintraege;
        Assert.True(zeilen.Single(z => z.Id == id).Aufgehoben);
        Assert.True(zeilen.Single(z => z.Id == gegen.Id).IstGegenbuchung);
    }

    [Fact]
    public async Task Laufender_Saldo_Filter_und_Bericht()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.ZahlenAsync(w.Org.Schueler, 50_000, datum: new(2026, 10, 1));
        await w.ZahlenAsync(w.Org.SchuelerZiel, 10_000, ZahlungsKanal.MobileMoney, new(2026, 11, 5));
        await w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 10, 20), await KategorieAsync(w, "Loyer"), 20_000, "Loyer"), null);
        await w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 10), await KategorieAsync(w, "Dons et subventions"), 5_000, "Don"), null);

        var seite = await w.Journal.SucheAsync(w.D, new JournalFilter(null, null, null, null, null), 0, 2);
        Assert.True(seite.Mehr);
        Assert.Equal([45_000L, 40_000], seite.Eintraege.Select(e => e.SaldoNach!.Value));
        var rest = await w.Journal.SucheAsync(w.D, new JournalFilter(null, null, null, null, null), 2, 2);
        Assert.Equal([30_000L, 50_000], rest.Eintraege.Select(e => e.SaldoNach!.Value));
        var depenses = await w.Journal.SucheAsync(w.D, new JournalFilter(null, null, BuchungsRichtung.Depense, null, null), 0, 50);
        Assert.Null(Assert.Single(depenses.Eintraege).SaldoNach);
        var summen = await w.Journal.SummenAsync(w.D, new JournalFilter(new(2026, 11, 1), new(2026, 11, 30), null, null, null));
        Assert.Equal((15_000L, 0L), (summen.Recettes, summen.Depenses));
        Assert.Equal(new KassenStand(45_000, 65_000, 20_000), await w.Journal.KasseAsync(w.D, w.Org.Jahr));

        var bericht = await w.Bericht.BerichtAsync(w.D, new(2026, 11, 1), new(2026, 11, 30));
        Assert.Equal((30_000L, 15_000L, 45_000L), (bericht.SaldoAnfang, bericht.TotalRecettes, bericht.SaldoEnde));
        Assert.Equal(["Mobile Money"], bericht.Kanaele.Select(k => k.Kanal));
        Assert.Equal(140_000 - 60_000, bericht.Forderungen);
        Assert.Equal(1, bericht.SchuelerEnRetard);
        Assert.Equal((20_000L, 60_000L), (Assert.Single(bericht.Rueckstaende).EnRetard, bericht.Rueckstaende[0].Offen));
        Assert.Equal("%PDF"u8.ToArray(), (await w.Bericht.PdfAsync(w.D, new(2026, 9, 1), new(2026, 11, 13))).Inhalt[..4]);
        await Assert.ThrowsAsync<FachFehler>(() => w.Bericht.BerichtAsync(w.D, new(2026, 11, 30), new(2026, 11, 1)));
    }

    [Fact]
    public async Task Eigene_Kategorien()
    {
        var w = await FinanzWelt.ErstelleAsync();
        var id = await w.Journal.KategorieAnlegenAsync(w.KMit, "Cantine", BuchungsRichtung.Recette);
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.KategorieAnlegenAsync(w.D, "Cantine", BuchungsRichtung.Recette));
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.KategorieAnlegenAsync(w.D, "Loyer", BuchungsRichtung.Depense));
        Assert.DoesNotContain(await w.MitDbAsync(db => db.Kategorien.Where(k => k.Name == "Cantine").Select(k => k.SchuleId).ToListAsync()), s => s != w.Org.SchuleA);
        await w.Journal.ErfassenAsync(w.D, new BuchungEingabe(new(2026, 11, 3), id, 2_000, "Repas"), null);
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.KategorieLoeschenAsync(w.D, id));
        var loyer = await KategorieAsync(w, "Loyer");
        await Assert.ThrowsAsync<FachFehler>(() => w.Journal.KategorieLoeschenAsync(w.D, loyer));
        var leer = await w.Journal.KategorieAnlegenAsync(w.D, "Divers", BuchungsRichtung.Depense);
        await w.Journal.KategorieLoeschenAsync(w.D, leer);
        Assert.DoesNotContain(await w.Journal.KategorienAsync(w.D), k => k.Id == leer);
    }
}
