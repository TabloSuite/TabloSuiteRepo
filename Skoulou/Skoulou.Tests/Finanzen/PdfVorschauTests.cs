using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Services.Finanzen;

namespace Skoulou.Tests.Finanzen;

/// <summary>Quittung, Rechnung und Bericht als echte PDFs; mit SKOULOU_PDF_BILDER=&lt;Ordner&gt; zusätzlich als PNG (Sichtprüfung).</summary>
public class PdfVorschauTests
{
    private static void Vorschau(IDocument dokument, string name)
    {
        if (Environment.GetEnvironmentVariable("SKOULOU_PDF_BILDER") is not { Length: > 0 } ordner) return;
        Directory.CreateDirectory(ordner);
        dokument.GenerateImages(i => Path.Combine(ordner, $"{name}-{i}.png"), new ImageGenerationSettings { RasterDpi = 130 });
    }

    [Fact]
    public async Task Quittung_Rechnung_und_Bericht_werden_erzeugt()
    {
        var w = await FinanzWelt.ErstelleAsync();
        await w.ZahlenAsync(w.Org.Schueler, 10_000, datum: new(2026, 9, 2));
        var z = await w.ZahlenAsync(w.Org.Schueler, 35_000, ZahlungsKanal.MobileMoney, new(2026, 11, 12));
        var quittung = await w.Quittungen.DatenAsync(w.D, z.ZahlungId);
        Assert.Equal("trente-cinq mille francs CFA", Skoulou.Core.Werkzeuge.ZahlInWorten.Fcfa(quittung.Betrag));
        Assert.Equal((45_000L, 25_000L), (quittung.BezahltBisher, quittung.RestNachher));
        Vorschau(QuittungPdf.Dokument(quittung), "e6-recu");

        await w.Zahlungen.StornierenAsync(w.D, z.ZahlungId, "Erreur de saisie du montant");
        Vorschau(QuittungPdf.Dokument(await w.Quittungen.DatenAsync(w.D, z.ZahlungId)), "e6-recu-annule");

        var id = (await w.Rechnungen.JetztAbrechnenAsync(w.Org.Admin, w.Org.P1))!.Value;
        var detail = await w.Rechnungen.DetailAsync(w.Org.Admin, id);
        var schule = await w.MitDbAsync(db => QuittungService.SchulDokumentAsync(db, w.Org.D.Speicher, w.Org.SchuleA, default));
        var rechnung = RechnungPdf.Erzeuge(detail, schule, "skoulou.test");
        Assert.Equal("%PDF"u8.ToArray(), rechnung[..4]);
        Vorschau(RechnungPdf.Dokument(detail, schule, "skoulou.test"), "e6-facture");

        var bericht = await w.Bericht.BerichtAsync(w.D, new(2026, 9, 1), new(2026, 11, 13));
        Vorschau(BerichtPdf.Dokument(bericht), "e6-rapport");
        Assert.Equal("%PDF"u8.ToArray(), BerichtPdf.Erzeuge(bericht)[..4]);
    }
}
