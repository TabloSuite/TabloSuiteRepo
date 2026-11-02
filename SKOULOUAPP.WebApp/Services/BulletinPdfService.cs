using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace SkoulouApp.WebApp.Services;

public sealed class BulletinPdfService
{
    public byte[] Erzeugen(BulletinSnapshot bulletin) => Dokument(bulletin).GeneratePdf();

    public byte[] SammelausgabeErzeugen(IReadOnlyCollection<BulletinSnapshot> bulletins)
    {
        if (bulletins.Count is 0 or > 200)
            throw new InvalidOperationException("Sélectionnez entre 1 et 200 bulletins publiés.");
        return Document.Merge(bulletins.Select(Dokument)).UseOriginalPageNumbers().GeneratePdf();
    }

    private static Document Dokument(BulletinSnapshot bulletin) => Document.Create(document => document.Page(page =>
    {
        page.Size(PageSizes.A4); page.Margin(Math.Clamp(bulletin.SeitenRand, 15, 50)); page.DefaultTextStyle(x => x.FontSize(Math.Clamp(bulletin.SchriftGroesse, 8, 12)));
        page.Header().Column(header =>
        {
            if (bulletin.Logo is { Length: > 0 }) header.Item().AlignCenter().Width(55).Height(45).Image(bulletin.Logo).FitArea();
            header.Item().AlignCenter().Text(bulletin.Republik ?? "").SemiBold();
            header.Item().AlignCenter().Text(bulletin.Motto ?? "");
            header.Item().AlignCenter().Text(bulletin.Ministerium ?? "").FontSize(9);
            header.Item().PaddingTop(8).AlignCenter().Text(bulletin.Schule).Bold().FontSize(20).FontColor(bulletin.Farbe);
            header.Item().AlignCenter().Text(bulletin.Slogan ?? "").Italic();
            header.Item().AlignCenter().Text($"{bulletin.Adresse}\n{bulletin.Kontakt}\n{bulletin.Zulassung}").FontSize(9);
            header.Item().PaddingVertical(8).AlignCenter().Text($"BULLETIN DE NOTES · {bulletin.Titel}").Bold().FontSize(14);
        });
        page.Content().Column(content =>
        {
            content.Spacing(8);
            foreach (var block in bulletin.BlockReihenfolge.Split(','))
            {
            if (block == "Schueler")
            {
            content.Item().Text($"Année scolaire : {bulletin.Schuljahr} · Classe : {bulletin.Klasse}");
            content.Item().Text($"Nom et prénoms : {bulletin.Schueler}").Bold();
            content.Item().Text($"Matricule : {bulletin.Matrikelnummer ?? "—"} · Né(e) le : {bulletin.Geburtsdatum ?? "—"} à {bulletin.Geburtsort ?? "—"}");
            content.Item().Text($"Statut : {(bulletin.IstWiederholer ? "Redoublant" : "Non redoublant")} · Effectif : {bulletin.AnzahlSchueler}");
            }
            if (block == "Noten")
            content.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); if (bulletin.Koeffizienten) c.RelativeColumn(); if (bulletin.ZeigeLehrkraefte) c.RelativeColumn(2); });
                table.Header(h =>
                {
                    h.Cell().Background(bulletin.Farbe).Padding(5).Text("Matière").FontColor(Colors.White);
                    h.Cell().Background(bulletin.Farbe).Padding(5).Text("Note / 20").FontColor(Colors.White);
                    if (bulletin.Koeffizienten) h.Cell().Background(bulletin.Farbe).Padding(5).Text("Coef.").FontColor(Colors.White);
                    if (bulletin.ZeigeLehrkraefte) h.Cell().Background(bulletin.Farbe).Padding(5).Text("Enseignant").FontColor(Colors.White);
                });
                foreach (var fach in bulletin.Faecher)
                {
                    table.Cell().BorderBottom(0.5f).Padding(5).Text(fach.Fach);
                    table.Cell().BorderBottom(0.5f).Padding(5).Text(Note(fach.Note));
                    if (bulletin.Koeffizienten) table.Cell().BorderBottom(0.5f).Padding(5).Text(fach.Koeffizient.ToString("0.##"));
                    if (bulletin.ZeigeLehrkraefte) table.Cell().BorderBottom(0.5f).Padding(5).Text(fach.Lehrer);
                }
            });
            if (block == "Ergebnisse")
            {
            content.Item().Text($"Moyenne : {Note(bulletin.Durchschnitt)} / 20").Bold().FontSize(13);
            if (bulletin.RangAnzeigen) content.Item().Text($"Rang : {bulletin.Rang?.ToString() ?? "Non calculable (notes manquantes)"} / {bulletin.AnzahlSchueler}");
            if (bulletin.VorperiodenAnzeigen)
                foreach (var periode in bulletin.Vorperioden) content.Item().Text($"{periode.Titel} : {Note(periode.Note)} / 20");
            if (bulletin.JahresNote is not null) content.Item().Text($"Moyenne annuelle : {Note(bulletin.JahresNote)} / 20").Bold();
            if (bulletin.ZeigeAnwesenheit && bulletin.ErfassteTage > 0) content.Item().Text($"Présences renseignées : {bulletin.ErfassteTage} jours · Absences : {bulletin.FehlTage} (justifiées : {bulletin.EntschuldigteFehlTage}) · Retards : {bulletin.Verspaetungen} ({bulletin.VerspaetungsMinuten} min)");
            }
            if (block == "Bemerkung") content.Item().PaddingTop(8).Text($"Observation : {bulletin.Bemerkung}");
            if (block == "Unterschriften" && bulletin.Unterschriften) content.Item().PaddingTop(25).Row(row =>
            {
                row.RelativeItem().Text("Le professeur titulaire\n\n\nSignature");
                row.RelativeItem().Text("Le parent / responsable\n\n\nSignature");
                row.RelativeItem().Column(direction => {
                    // Bereits gespeicherte Dokumentversionen behalten ihren historischen Bildstand.
                    if (bulletin.Siegel is { Length: > 0 }) direction.Item().Width(65).Height(45).Image(bulletin.Siegel).FitArea();
                    else direction.Item().Height(65);
                    direction.Item().Text("Signature et cachet de la direction");
                });
            });
            }
        });
        page.Footer().Column(footer =>
        {
            footer.Item().Text(bulletin.FussText ?? "").FontSize(8);
            footer.Item().AlignRight().Text(t => { t.Span("Page "); t.CurrentPageNumber(); });
        });
    }));

    private static string Note(decimal? valeur) => valeur is null ? "—" : Core.Services.NotenBerechnung.Anzeige(valeur.Value).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
}
