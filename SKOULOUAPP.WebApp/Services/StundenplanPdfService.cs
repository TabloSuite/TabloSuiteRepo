using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace SkoulouApp.WebApp.Services;

public sealed class StundenplanPdfService
{
    public byte[] Erzeugen(IReadOnlyCollection<StundenplanZeile> zeilen) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(24);
        page.DefaultTextStyle(x => x.FontSize(10));
        page.Header().Text("SKOULOUAPP · Emploi du temps").Bold().FontSize(18);
        page.Content().PaddingTop(15).Column(column =>
        {
            column.Spacing(8);
            foreach (var plan in zeilen.GroupBy(x => new { x.PlanID, x.Plan, x.Klasse, x.GueltigAb, x.GueltigBis, x.IstVeroeffentlicht }))
            {
                column.Item().Text($"{plan.Key.Plan} · {plan.Key.Klasse}").Bold();
                column.Item().Text($"{plan.Key.GueltigAb:dd/MM/yyyy} – {plan.Key.GueltigBis:dd/MM/yyyy} · {(plan.Key.IstVeroeffentlicht ? "Publié" : "Brouillon")}");
                foreach (var cours in plan.OrderBy(x => ((int)x.Wochentag + 6) % 7).ThenBy(x => x.Beginn))
                {
                    var jour = new System.Globalization.CultureInfo("fr-FR").DateTimeFormat.GetDayName(cours.Wochentag);
                    column.Item().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5)
                        .Text($"{jour} · {cours.Beginn:HH:mm}–{cours.Ende:HH:mm}\n{cours.Fach} · {cours.Lehrer} · {cours.Raum}");
                }
            }
        });
        page.Footer().AlignRight().Text(text => { text.Span("Page "); text.CurrentPageNumber(); });
    })).GeneratePdf();
}
