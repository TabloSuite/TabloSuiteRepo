using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Rechte;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Services.Organisation.Druck;

/// <summary>
/// « Liste des accès » (A4): je Konto ein ausschneidbarer Abschnitt mit Name, Rolle, Zusatz, Identifiant,
/// Startpasswort (Festbreitenschrift) und Anmeldeadresse. Wird nur aus der Kurzzeit-Ablage gedruckt.
/// </summary>
internal static class ZugangslistePdf
{
    public static byte[] Erzeuge(SchulKopf schule, IReadOnlyList<Zugang> zugaenge, string anmeldeUrl, DateOnly datum)
        => Dokument(schule, zugaenge, anmeldeUrl, datum).GeneratePdf();

    /// <summary>Dokument (für PDF oder Vorschaubilder in Tests).</summary>
    public static IDocument Dokument(SchulKopf schule, IReadOnlyList<Zugang> zugaenge, string anmeldeUrl, DateOnly datum)
        => Document.Create(doc => doc.Page(page =>
        {
            DruckStil.Seite(page, PageSizes.A4, 14);
            page.Header().Element(c => DruckStil.Kopf(c, schule, "Liste des accès",
                $"{zugaenge.Count} compte{(zugaenge.Count > 1 ? "s" : "")} · éditée le {SkoulouKultur.Datum(datum)}"));
            page.Content().Column(col =>
            {
                col.Spacing(0);
                col.Item().PaddingBottom(3, Unit.Millimetre).Background(DruckStil.WarnZart).Padding(2.5f, Unit.Millimetre)
                    .Text("Document confidentiel : découpez les fiches et remettez chacune en main propre. Ces mots de passe de départ ne sont imprimés qu'une seule fois.")
                    .FontSize(8.5f).FontColor(DruckStil.Warn);
                foreach (var z in zugaenge)
                    col.Item().ShowEntire().Element(c => Fiche(c, z, anmeldeUrl));
                col.Item().Element(DruckStil.Schnittlinie);
            });
            page.Footer().Element(c => DruckStil.Fuss(c, $"{schule.Name} · SKOULOU"));
        }));

    private static void Fiche(IContainer container, Zugang z, string anmeldeUrl)
    {
        container.Column(col =>
        {
            col.Item().Element(DruckStil.Schnittlinie);
            col.Item().PaddingVertical(3.5f, Unit.Millimetre).PaddingHorizontal(1, Unit.Millimetre).Row(row =>
            {
                row.RelativeItem(5).Column(links =>
                {
                    links.Item().Text(z.Name).FontSize(12.5f).Bold().FontColor(DruckStil.PrimaerDunkel);
                    links.Item().Text(text =>
                    {
                        text.Span(Rollen.Bezeichnung(z.Rolle)).SemiBold().FontColor(DruckStil.Akzent);
                        if (!string.IsNullOrWhiteSpace(z.Zusatz)) text.Span($" · {z.Zusatz}").FontColor(DruckStil.Leise);
                    });
                    links.Item().PaddingTop(2, Unit.Millimetre).Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(8).FontColor(DruckStil.Leise));
                        text.Span("Connexion : ");
                        text.Span(anmeldeUrl).SemiBold().FontColor(DruckStil.Primaer);
                    });
                    links.Item().Text("À la première connexion, choisissez votre propre mot de passe.").FontSize(8).FontColor(DruckStil.Leise);
                });
                row.ConstantItem(4, Unit.Millimetre);
                row.RelativeItem(4).Background(DruckStil.Flaeche).Border(0.6f).BorderColor(DruckStil.RandZart).Padding(2.5f, Unit.Millimetre).Column(rechts =>
                {
                    Wert(rechts, "Identifiant", z.Identifiant);
                    rechts.Item().Height(1.5f, Unit.Millimetre);
                    Wert(rechts, "Mot de passe de départ", z.Passwort);
                });
            });
        });
    }

    private static void Wert(ColumnDescriptor col, string label, string wert)
    {
        col.Item().Text(label).FontSize(7.5f).FontColor(DruckStil.Leise);
        col.Item().Text(wert).FontFamily(DruckStil.Festbreite).FontSize(13).Bold().LetterSpacing(0.06f).FontColor(DruckStil.Text);
    }
}
