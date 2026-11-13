using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Organisation;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Services.Organisation.Druck;

/// <summary>
/// Wochenraster (A4 quer): Spalten Montag–Freitag (Samstag nur mit Stunden), Zeilen je Beginnzeit; jede Stunde
/// mit Fach, Uhrzeit, Lehrkraft (bzw. Klasse im Plan einer Lehrkraft) und Raum.
/// </summary>
internal static class StundenplanPdf
{
    public static byte[] Erzeuge(SchulKopf schule, string titel, string untertitel, IReadOnlyList<StundeZeile> stunden, bool mitKlasse, DateOnly datum)
        => Dokument(schule, titel, untertitel, stunden, mitKlasse, datum).GeneratePdf();

    /// <summary>Dokument (für PDF oder Vorschaubilder in Tests).</summary>
    public static IDocument Dokument(SchulKopf schule, string titel, string untertitel, IReadOnlyList<StundeZeile> stunden, bool mitKlasse, DateOnly datum)
        => Document.Create(doc => doc.Page(page =>
        {
            DruckStil.Seite(page, PageSizes.A4.Landscape(), 10);
            page.Header().Element(c => DruckStil.Kopf(c, schule, titel, untertitel));
            page.Content().Element(c => Raster(c, stunden, mitKlasse));
            page.Footer().Element(c => DruckStil.Fuss(c, $"{schule.Name} · édité le {SkoulouKultur.Datum(datum)}"));
        }));

    private static void Raster(IContainer container, IReadOnlyList<StundeZeile> stunden, bool mitKlasse)
    {
        if (stunden.Count == 0)
        {
            container.PaddingTop(12, Unit.Millimetre).AlignCenter().Text("Aucun cours dans cet emploi du temps.").FontSize(12).FontColor(DruckStil.Leise);
            return;
        }
        var tage = Wochentage.Schulwoche.Where(t => t != DayOfWeek.Saturday || stunden.Any(s => s.Wochentag == DayOfWeek.Saturday))
            .Concat(stunden.Any(s => s.Wochentag == DayOfWeek.Sunday) ? [DayOfWeek.Sunday] : []).ToList();
        var zeiten = stunden.Select(s => s.Beginn).Distinct().OrderBy(z => z).ToList();

        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(18, Unit.Millimetre);
                foreach (var _ in tage) c.RelativeColumn();
            });
            table.Header(h =>
            {
                h.Cell().Element(Kopfzelle).Text("Heure");
                foreach (var t in tage) h.Cell().Element(Kopfzelle).Text(Wochentage.Name(t));
            });
            foreach (var zeit in zeiten)
            {
                table.Cell().Element(Zeitzelle).Text(SkoulouKultur.Uhrzeit(zeit)).SemiBold().FontColor(DruckStil.Leise);
                foreach (var t in tage)
                {
                    var hier = stunden.Where(s => s.Wochentag == t && s.Beginn == zeit).OrderBy(s => s.Ende).ToList();
                    table.Cell().Element(Zelle).Column(col =>
                    {
                        col.Spacing(1.5f);
                        foreach (var s in hier) col.Item().Element(c => Block(c, s, mitKlasse));
                    });
                }
            }
        });
    }

    private static void Block(IContainer container, StundeZeile s, bool mitKlasse)
    {
        container.Background(DruckStil.PrimaerZart).BorderLeft(2.5f).BorderColor(DruckStil.Primaer).Padding(1.8f, Unit.Millimetre).Column(col =>
        {
            col.Item().Text(s.Kuerzel is { Length: > 0 } k && s.Fach.Length > 18 ? k : s.Fach).FontSize(9).Bold().FontColor(DruckStil.PrimaerDunkel).ClampLines(2);
            col.Item().Text($"{SkoulouKultur.Uhrzeit(s.Beginn)} – {SkoulouKultur.Uhrzeit(s.Ende)}").FontSize(7.5f).FontColor(DruckStil.Leise);
            col.Item().Text(mitKlasse ? s.Klasse : s.Lehrer).FontSize(7.5f).ClampLines(1);
            if (!string.IsNullOrWhiteSpace(s.Raum)) col.Item().Text(RaumText(s.Raum)).FontSize(7.5f).FontColor(DruckStil.Akzent);
        });
    }

    /// <summary>« Salle 3 » bleibt, « B12 » wird « Salle B12 ».</summary>
    public static string RaumText(string raum) => raum.StartsWith("salle", StringComparison.OrdinalIgnoreCase) ? raum : $"Salle {raum}";

    private static IContainer Kopfzelle(IContainer c)
        => c.Background(DruckStil.Primaer).PaddingVertical(2, Unit.Millimetre).PaddingHorizontal(1.5f, Unit.Millimetre)
            .DefaultTextStyle(s => s.FontSize(9).Bold().FontColor(Colors.White));

    private static IContainer Zeitzelle(IContainer c)
        => c.BorderBottom(0.5f).BorderColor(DruckStil.RandZart).Background(DruckStil.Flaeche).Padding(1.5f, Unit.Millimetre).DefaultTextStyle(s => s.FontSize(8.5f));

    private static IContainer Zelle(IContainer c)
        => c.BorderBottom(0.5f).BorderLeft(0.5f).BorderColor(DruckStil.RandZart).Padding(1.2f, Unit.Millimetre);
}
