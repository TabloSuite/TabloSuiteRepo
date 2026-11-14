using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Finanzen;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Organisation.Druck;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>« Rapport financier » (A4): Kassenbewegung nach Kategorie, Zahlungen nach Kanal, Rückstände der Familien.</summary>
internal static class BerichtPdf
{
    public static byte[] Erzeuge(BerichtDaten d) => Dokument(d).GeneratePdf();

    public static IDocument Dokument(BerichtDaten d) => FinanzDruck.Lizenz(Document.Create(doc => doc.Page(page =>
    {
        DruckStil.Seite(page, PageSizes.A4, 14);
        page.Header().Element(c => FinanzDruck.Kopf(c, d.Schule, "RAPPORT FINANCIER", $"Du {SkoulouKultur.Datum(d.Von)} au {SkoulouKultur.Datum(d.Bis)}"));
        page.Content().PaddingTop(4, Unit.Millimetre).Column(col =>
        {
            col.Spacing(4, Unit.Millimetre);
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Solde au début", SkoulouKultur.Fcfa(d.SaldoAnfang)));
                row.ConstantItem(3, Unit.Millimetre);
                row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Recettes", SkoulouKultur.Fcfa(d.TotalRecettes)));
                row.ConstantItem(3, Unit.Millimetre);
                row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Dépenses", SkoulouKultur.Fcfa(d.TotalDepenses)));
                row.ConstantItem(3, Unit.Millimetre);
                row.RelativeItem().Element(c => FinanzDruck.Kennzahl(c, "Solde à la fin", SkoulouKultur.Fcfa(d.SaldoEnde), hervor: true));
            });
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Tabelle(c, "Recettes par catégorie", d.Recettes.Select(r => (r.Kategorie, r.Betrag)), d.TotalRecettes));
                row.ConstantItem(6, Unit.Millimetre);
                row.RelativeItem().Element(c => Tabelle(c, "Dépenses par catégorie", d.Depenses.Select(r => (r.Kategorie, r.Betrag)), d.TotalDepenses));
            });
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Tabelle(c, "Paiements des familles par moyen",
                    d.Kanaele.Select(k => ($"{k.Kanal} ({k.Anzahl})", k.Betrag)), d.Kanaele.Sum(k => k.Betrag)));
                row.ConstantItem(6, Unit.Millimetre);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Créances des familles").FontSize(10).Bold().FontColor(DruckStil.PrimaerDunkel);
                    c.Item().PaddingTop(1, Unit.Millimetre).Text(d.Schuljahr is null ? "Aucune année scolaire." : $"Année {d.Schuljahr}, au {SkoulouKultur.Datum(d.Erstellt)}")
                        .FontSize(8).FontColor(DruckStil.Leise);
                    c.Item().Text($"Reste à encaisser : {SkoulouKultur.Fcfa(d.Forderungen)}").FontSize(9);
                    c.Item().Text($"Dont échu (en retard) : {SkoulouKultur.Fcfa(d.EnRetard)} · {d.SchuelerEnRetard} élève{(d.SchuelerEnRetard > 1 ? "s" : "")}").FontSize(9);
                    if (d.Stornos > 0)
                        c.Item().PaddingTop(1, Unit.Millimetre).Text($"Paiements annulés sur la période : {d.Stornos} ({SkoulouKultur.Fcfa(d.StornoBetrag)})").FontSize(8).FontColor(DruckStil.Leise);
                });
            });
            if (d.Rueckstaende.Count > 0)
            {
                col.Item().Text("Plus grands retards de paiement").FontSize(10).Bold().FontColor(DruckStil.PrimaerDunkel);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                    foreach (var titel in new[] { "Élève", "Classe", "Reste à payer", "En retard" })
                        t.Cell().BorderBottom(1).BorderColor(DruckStil.Rand).PaddingVertical(2).Text(titel).FontSize(8).SemiBold().FontColor(DruckStil.Leise);
                    foreach (var r in d.Rueckstaende)
                    {
                        t.Cell().Element(Zelle).Text(r.Name);
                        t.Cell().Element(Zelle).Text(r.Klasse ?? "—");
                        t.Cell().Element(Zelle).AlignRight().Text(SkoulouKultur.Fcfa(r.Offen));
                        t.Cell().Element(Zelle).AlignRight().Text(SkoulouKultur.Fcfa(r.EnRetard)).FontColor(DruckStil.Warn);
                    }
                });
            }
            col.Item().Text("Le solde de caisse ne contient que les mouvements réels d'argent. Les créances des familles et les factures SKOULOU non réglées sont suivies à part.")
                .FontSize(7.5f).Italic().FontColor(DruckStil.Leise);
        });
        page.Footer().Element(c => DruckStil.Fuss(c, $"{d.Schule.Name} · rapport édité le {SkoulouKultur.Datum(d.Erstellt)} avec SKOULOU"));
    })));

    private static void Tabelle(IContainer container, string titel, IEnumerable<(string Text, long Betrag)> zeilen, long total)
    {
        container.Column(c =>
        {
            c.Item().PaddingBottom(1, Unit.Millimetre).Text(titel).FontSize(10).Bold().FontColor(DruckStil.PrimaerDunkel);
            var liste = zeilen.ToList();
            if (liste.Count == 0) c.Item().Text("Aucun mouvement.").FontSize(8.5f).FontColor(DruckStil.Leise);
            foreach (var (text, betrag) in liste)
                c.Item().Element(Zelle).Row(r =>
                {
                    r.RelativeItem().Text(text);
                    r.ConstantItem(32, Unit.Millimetre).AlignRight().Text(SkoulouKultur.Fcfa(betrag));
                });
            c.Item().PaddingTop(1).Row(r =>
            {
                r.RelativeItem().Text("Total").SemiBold();
                r.ConstantItem(32, Unit.Millimetre).AlignRight().Text(SkoulouKultur.Fcfa(total)).Bold();
            });
        });
    }

    private static IContainer Zelle(IContainer c) => c.BorderBottom(0.5f).BorderColor(DruckStil.RandZart).PaddingVertical(2).DefaultTextStyle(s => s.FontSize(8.5f));
}
