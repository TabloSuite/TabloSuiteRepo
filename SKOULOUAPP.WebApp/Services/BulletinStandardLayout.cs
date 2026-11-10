using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static SkoulouApp.WebApp.Services.BulletinTexte;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Allgemeines Bulletin-Layout, zusammengeführt aus Mustern aus Côte d'Ivoire und Togo:
/// dreispaltiger Kopf (Ministerium · Titel · Republik), Schülerblock, Notentabelle mit
/// Teilnoten, Moy × Coef, Rang, Gruppen-Bilans und Totaux, darunter Assiduité, Moyenne,
/// Résultats de la classe, Mentions, Appréciation du conseil, Visa und Jahresbilanz.
/// </summary>
internal static class BulletinStandardLayout
{
    private const string Rahmen = "#4b5563";
    private const string Hell = "#f3f4f6";

    public static void Seite(PageDescriptor page, BulletinSnapshot b, Action<ColumnDescriptor> fusszeileQr)
    {
        var layout = b.Layout!;
        var farbe = b.Farbe;
        page.Size(PageSizes.A4);
        page.Margin(Math.Clamp(b.SeitenRand, 15, 40));
        page.DefaultTextStyle(x => x.FontSize(Math.Clamp(b.SchriftGroesse - 1, 7, 10)).FontColor("#111827"));

        page.Header().Element(kopf => Kopf(kopf, b));
        page.Content().PaddingTop(6).Column(inhalt =>
        {
            inhalt.Spacing(6);
            inhalt.Item().Element(e => SchuelerBlock(e, b, layout));
            inhalt.Item().Element(e => NotenTabelle(e, b, layout, farbe));
            inhalt.Item().Element(e => Ergebnisse(e, b, layout));
            if (layout.JahresBilanz && b.IstJahresabschluss)
                inhalt.Item().Element(e => Jahresbilanz(e, b));
        });
        page.Footer().Column(fusszeile =>
        {
            if (!string.IsNullOrWhiteSpace(b.FussText)) fusszeile.Item().AlignCenter().Text(b.FussText).FontSize(7).Italic();
            fusszeileQr(fusszeile);
            fusszeile.Item().AlignRight().Text(t => { t.Span("Page ").FontSize(7); t.CurrentPageNumber().FontSize(7); });
        });
    }

    private static void Kopf(IContainer container, BulletinSnapshot b)
    {
        container.Column(kopf =>
        {
            kopf.Item().Row(zeile =>
            {
                zeile.RelativeItem(3).Column(links =>
                {
                    if (!string.IsNullOrWhiteSpace(b.Ministerium)) links.Item().AlignCenter().Text(b.Ministerium).FontSize(7.5f).SemiBold();
                    if (!string.IsNullOrWhiteSpace(b.Direction)) links.Item().AlignCenter().Text(b.Direction).FontSize(7.5f);
                    if (b.Logo is { Length: > 0 }) links.Item().PaddingTop(3).AlignCenter().Height(38).Image(b.Logo).FitArea();
                });
                zeile.RelativeItem(4).PaddingHorizontal(6).Column(mitte =>
                {
                    mitte.Item().Border(1).BorderColor(Rahmen).PaddingVertical(4).PaddingHorizontal(6).Column(titel =>
                    {
                        titel.Item().AlignCenter().Text(Titelzeile(b)).Bold().FontSize(11);
                        titel.Item().AlignCenter().Text(b.Titel).SemiBold().FontSize(10);
                    });
                    mitte.Item().PaddingTop(4).AlignCenter().Text(b.Schule).Bold().FontSize(11).FontColor(b.Farbe);
                    if (!string.IsNullOrWhiteSpace(b.Slogan)) mitte.Item().AlignCenter().Text(b.Slogan).Italic().FontSize(7.5f);
                });
                zeile.RelativeItem(3).Column(rechts =>
                {
                    if (!string.IsNullOrWhiteSpace(b.Republik)) rechts.Item().AlignCenter().Text(b.Republik).FontSize(8).SemiBold();
                    if (!string.IsNullOrWhiteSpace(b.Motto)) rechts.Item().AlignCenter().Text(b.Motto).FontSize(7.5f).Italic();
                    rechts.Item().PaddingTop(4).AlignCenter().Text($"Année scolaire : {b.Schuljahr}").SemiBold().FontSize(8.5f);
                    if (!string.IsNullOrWhiteSpace(b.Zulassung)) rechts.Item().AlignCenter().Text($"Code : {b.Zulassung}").FontSize(7.5f);
                    if (!string.IsNullOrWhiteSpace(b.Statut)) rechts.Item().AlignCenter().Text($"Statut : {b.Statut}").FontSize(7.5f);
                });
            });

            var kontakt = string.Join("   ·   ", new[] { b.Adresse, b.Kontakt }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (kontakt.Length > 0)
                kopf.Item().PaddingTop(4).BorderTop(0.6f).BorderBottom(0.6f).BorderColor(Rahmen).PaddingVertical(2).AlignCenter().Text(kontakt).FontSize(7.5f);
            if (b.DokumentNummer is not null)
            {
                kopf.Item().PaddingTop(2).Row(z =>
                {
                    z.RelativeItem().Text($"N° {b.DokumentNummer} · {b.DokumentDatumUtc:dd/MM/yyyy}").FontSize(7);
                    if (b.ErsetztDokumentNummer is not null)
                        z.RelativeItem().AlignRight().Text($"BULLETIN RECTIFICATIF — remplace le n° {b.ErsetztDokumentNummer}").Bold().FontSize(7.5f);
                });
            }
        });
    }

    private static string Titelzeile(BulletinSnapshot b)
    {
        var titel = b.Titel.ToLowerInvariant();
        return titel.Contains("semestre") ? "BULLETIN SEMESTRIEL DE NOTES"
            : titel.Contains("trimestre") ? "BULLETIN TRIMESTRIEL DE NOTES"
            : "BULLETIN DE NOTES";
    }

    private static void SchuelerBlock(IContainer container, BulletinSnapshot b, BulletinLayout layout)
    {
        container.Border(1).BorderColor(Rahmen).Padding(5).Row(zeile =>
        {
            zeile.RelativeItem().Column(spalte =>
            {
                spalte.Spacing(1.5f);
                spalte.Item().Text(b.Schueler.ToUpperInvariant()).Bold().FontSize(12);
                spalte.Item().Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Matricule : "); t.Span(b.Matrikelnummer ?? "—").Bold(); });
                    r.RelativeItem().Text(t => { t.Span("Classe : "); t.Span(b.Klasse).Bold(); });
                    r.RelativeItem().Text(t => { t.Span("Effectif : "); t.Span(b.AnzahlSchueler.ToString()).Bold(); });
                });
                spalte.Item().Row(r =>
                {
                    r.RelativeItem().Text($"Sexe : {b.Geschlecht ?? "—"}");
                    r.RelativeItem(2).Text($"Né(e) le : {b.Geburtsdatum ?? "—"}   à : {b.Geburtsort ?? "—"}");
                });
                spalte.Item().Row(r =>
                {
                    r.RelativeItem().Text($"Nationalité : {b.Nationalitaet ?? "—"}");
                    r.RelativeItem().Text($"Redoublant(e) : {(b.IstWiederholer ? "Oui" : "Non")}");
                    r.RelativeItem().Text($"Interne : {(b.IstInterne ? "Oui" : "Non")}");
                    r.RelativeItem().Text($"Affecté(e) : {(b.IstAffecte ? "Oui" : "Non")}");
                });
            });
            if (layout.Foto)
            {
                zeile.ConstantItem(62).PaddingLeft(6).Height(70).Border(0.6f).BorderColor(Rahmen).AlignMiddle().AlignCenter().Element(foto =>
                {
                    if (b.Foto is { Length: > 0 }) foto.Image(b.Foto).FitArea();
                    else foto.Text("Photo").FontSize(7).FontColor("#9ca3af");
                });
            }
        });
    }

    private sealed record Spalte(string Titel, float Breite, Func<BulletinFach, string> Wert, bool Rechts = true);

    private static void NotenTabelle(IContainer container, BulletinSnapshot b, BulletinLayout layout, string farbe)
    {
        var teilnoten = layout.Teilnoten ? b.TeilnotenSpalten ?? [] : [];
        var jahr = layout.JahresBilanz && b.IstJahresabschluss;
        var spalten = new List<Spalte> { new("DISCIPLINES", 3.2f, f => f.Fach, false) };
        for (var i = 0; i < teilnoten.Count; i++)
        {
            var index = i;
            spalten.Add(new(teilnoten[i], 1f, f => Note(f.Teilnoten?.ElementAtOrDefault(index)?.Note)));
        }
        spalten.Add(new("MOY./20", 1.05f, f => Note(f.Note)));
        if (b.Koeffizienten) spalten.Add(new("COEF", 0.75f, f => f.Koeffizient.ToString("0.##")));
        if (layout.Punkte && b.Koeffizienten) spalten.Add(new("MOY×COEF", 1.15f, f => f.Note is null ? "—" : Note(f.Note * f.Koeffizient)));
        if (layout.FachRang) spalten.Add(new("RANG", 0.8f, f => Rang(f.Rang)));
        if (jahr) { spalten.Add(new("MOY. ANN.", 1.05f, f => Note(f.JahresNote))); if (layout.FachRang) spalten.Add(new("RANG ANN.", 0.8f, f => Rang(f.JahresRang))); }
        if (layout.Appreciation) spalten.Add(new("APPRÉCIATION", 1.7f, f => f.Appreciation ?? "", false));
        if (b.ZeigeLehrkraefte) spalten.Add(new("PROFESSEUR", 2.2f, f => f.Lehrer, false));
        if (layout.SignaturSpalte) spalten.Add(new("SIGNATURE", 1.2f, _ => "", false));

        // Fächer nach Gruppe ordnen (Reihenfolge der ersten Nennung), Fächer ohne Gruppe am Ende.
        var gruppen = layout.FachGruppen && b.Faecher.Any(f => !string.IsNullOrWhiteSpace(f.Gruppe))
            ? b.Faecher.GroupBy(f => string.IsNullOrWhiteSpace(f.Gruppe) ? null : f.Gruppe!.Trim())
                .OrderBy(g => GruppenRang(g.Key)).ToList()
            : [b.Faecher.GroupBy(_ => (string?)null).FirstOrDefault()!];

        container.Table(table =>
        {
            table.ColumnsDefinition(c => { foreach (var s in spalten) c.RelativeColumn(s.Breite); });
            table.Header(h =>
            {
                foreach (var s in spalten)
                    h.Cell().Background(farbe).Border(0.5f).BorderColor(Rahmen).PaddingVertical(3).PaddingHorizontal(2)
                        .AlignCenter().AlignMiddle().Text(s.Titel).FontColor(Colors.White).Bold().FontSize(6.5f);
            });

            foreach (var gruppe in gruppen.Where(g => g is not null))
            {
                foreach (var fach in gruppe)
                    foreach (var s in spalten)
                    {
                        var zelle = table.Cell().Border(0.5f).BorderColor(Rahmen).PaddingVertical(2).PaddingHorizontal(3);
                        (s.Rechts ? zelle.AlignCenter() : zelle.AlignLeft()).Text(s.Wert(fach)).FontSize(s.Titel == "DISCIPLINES" ? 8 : 7.5f);
                    }

                if (gruppe.Key is not null)
                    BilanZeile(table, spalten, $"BILAN {gruppe.Key.ToUpperInvariant()}", gruppe.ToList(), Hell);
            }

            if (b.Koeffizienten)
                BilanZeile(table, spalten, "TOTAUX", b.Faecher, "#e5e7eb", nurSummen: true);
        });
    }

    /// <summary>Übliche Reihenfolge: Lettres, Sciences, weitere Gruppen, Autres, ohne Gruppe.</summary>
    private static int GruppenRang(string? gruppe) => gruppe?.ToLowerInvariant() switch
    {
        null => 99,
        "lettres" => 0,
        "sciences" => 1,
        "autres" => 9,
        _ => 5
    };

    private static void BilanZeile(TableDescriptor table, List<Spalte> spalten, string titel, List<BulletinFach> faecher, string hintergrund, bool nurSummen = false)
    {
        var mitNote = faecher.Where(f => f.Note is not null).ToList();
        var koef = mitNote.Sum(f => f.Koeffizient);
        var punkte = mitNote.Sum(f => f.Note!.Value * f.Koeffizient);
        decimal? mittel = koef > 0 ? punkte / koef : null;
        foreach (var s in spalten)
        {
            var text = s.Titel switch
            {
                "DISCIPLINES" => titel,
                "MOY./20" => nurSummen ? "" : Note(mittel),
                "COEF" => koef.ToString("0.##"),
                "MOY×COEF" => Note(punkte),
                _ => ""
            };
            var zelle = table.Cell().Background(hintergrund).Border(0.5f).BorderColor(Rahmen).PaddingVertical(2).PaddingHorizontal(3);
            (s.Rechts ? zelle.AlignCenter() : zelle.AlignLeft()).Text(text).Bold().FontSize(7.5f);
        }
    }

    private static void Kasten(IContainer container, string titel, Action<ColumnDescriptor> inhalt)
        => container.Border(1).BorderColor(Rahmen).Column(spalte =>
        {
            spalte.Item().Background(Hell).BorderBottom(0.6f).BorderColor(Rahmen).PaddingVertical(2).AlignCenter().Text(titel).Bold().FontSize(7.5f);
            spalte.Item().Padding(4).Column(inhalt);
        });

    private static void Ankreuzen(ColumnDescriptor spalte, string text, bool gewaehlt)
        => spalte.Item().Row(r =>
        {
            r.ConstantItem(9).Height(9).Border(0.7f).BorderColor(Rahmen).AlignCenter().AlignMiddle().Text(gewaehlt ? "X" : "").FontSize(7).Bold();
            r.RelativeItem().PaddingLeft(4).Text(text).FontSize(7.5f);
        });

    private static void Ergebnisse(IContainer container, BulletinSnapshot b, BulletinLayout layout)
    {
        container.Column(bereich =>
        {
            bereich.Spacing(5);
            bereich.Item().Row(zeile =>
            {
                zeile.Spacing(5);
                if (b.ZeigeAnwesenheit)
                {
                    zeile.RelativeItem().Element(e => Kasten(e, "ASSIDUITÉ", s =>
                    {
                        if (b.ErfassteTage == 0) { s.Item().Text("Présences non renseignées").FontSize(7.5f).FontColor("#6b7280"); return; }
                        s.Item().Text($"Absences : {b.FehlTage} jour(s)").FontSize(7.5f);
                        s.Item().Text($"dont justifiées : {b.EntschuldigteFehlTage} · non justifiées : {b.FehlTage - b.EntschuldigteFehlTage}").FontSize(7.5f);
                        s.Item().Text($"Retards : {b.Verspaetungen} ({b.VerspaetungsMinuten} min)").FontSize(7.5f);
                    }));
                }
                zeile.RelativeItem(1.2f).Element(e => Kasten(e, $"MOYENNE {PeriodenWort(b)}", s =>
                {
                    s.Item().AlignCenter().Text(t => { t.Span(Note(b.Durchschnitt)).Bold().FontSize(15); t.Span(" / 20").FontSize(9); });
                    if (layout.NoteInWorten && b.Durchschnitt is not null) s.Item().AlignCenter().Text(InWorten(b.Durchschnitt)).Italic().FontSize(7);
                    if (b.RangAnzeigen) s.Item().AlignCenter().Text(t => { t.Span("Rang : "); t.Span(Rang(b.Rang)).Bold(); t.Span($" sur {b.AnzahlSchueler}"); });
                    // Im letzten Bulletin des Jahres stehen die Vorperioden in der Jahresbilanz.
                    if (b.VorperiodenAnzeigen && !(layout.JahresBilanz && b.IstJahresabschluss))
                        foreach (var periode in b.Vorperioden) s.Item().AlignCenter().Text($"{periode.Titel} : {Note(periode.Note)} / 20").FontSize(7);
                }));
                if (layout.KlassenStatistik)
                {
                    zeile.RelativeItem().Element(e => Kasten(e, "RÉSULTATS DE LA CLASSE", s =>
                    {
                        s.Item().Text($"Moyenne de la classe : {Note(b.KlassenMittel)}").FontSize(7.5f);
                        s.Item().Text($"Plus forte moyenne : {Note(b.KlassenMax)}").FontSize(7.5f);
                        s.Item().Text($"Plus faible moyenne : {Note(b.KlassenMin)}").FontSize(7.5f);
                    }));
                }
            });

            bereich.Item().Row(zeile =>
            {
                zeile.Spacing(5);
                if (layout.Mentionen)
                {
                    zeile.RelativeItem().Element(e => Kasten(e, "MENTIONS DU CONSEIL DE CLASSE", s =>
                    {
                        s.Item().Text("Distinctions").SemiBold().FontSize(7.5f);
                        foreach (var text in new[] { "Tableau d'honneur", "Tableau d'honneur + Encouragements", "Tableau d'honneur + Félicitations" })
                            Ankreuzen(s, text, b.Distinktion == text);
                        s.Item().PaddingTop(3).Text("Sanctions").SemiBold().FontSize(7.5f);
                        foreach (var (_, text) in Sanktionen)
                            Ankreuzen(s, text, b.Sanktionen?.Contains(text) == true);
                    }));
                }
                zeile.RelativeItem().Element(e => Kasten(e, "APPRÉCIATION DU CONSEIL DE CLASSE", s =>
                {
                    var text = string.Join("\n", new[] { b.RatsBemerkung, b.Bemerkung }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    s.Item().MinHeight(60).Text(text).FontSize(8);
                }));
                if (b.Unterschriften)
                {
                    zeile.RelativeItem().Element(e => Kasten(e, "VISA DU CHEF D'ÉTABLISSEMENT", s =>
                    {
                        s.Item().Text($"Fait à {b.Ort ?? "…………"}, le {(b.DokumentDatumUtc ?? DateTime.UtcNow):dd/MM/yyyy}").FontSize(7.5f);
                        s.Item().Height(55);
                        s.Item().AlignCenter().Text("Signature et cachet").FontSize(7).FontColor("#6b7280");
                    }));
                }
            });
        });
    }

    private static void Jahresbilanz(IContainer container, BulletinSnapshot b)
    {
        container.Row(zeile =>
        {
            zeile.Spacing(5);
            zeile.RelativeItem(1.3f).Element(e => Kasten(e, "BILAN DE FIN D'ANNÉE", s =>
            {
                foreach (var periode in b.Vorperioden) s.Item().Text($"Moyenne {periode.Titel} : {Note(periode.Note)} / 20").FontSize(7.5f);
                s.Item().Text($"Moyenne {b.Titel} : {Note(b.Durchschnitt)} / 20").FontSize(7.5f);
                s.Item().PaddingTop(2).Text(t => { t.Span("Moyenne générale annuelle : ").Bold(); t.Span($"{Note(b.JahresNote)} / 20").Bold(); });
                s.Item().Text(t => { t.Span("Rang annuel : ").Bold(); t.Span(Rang(b.JahresRang)).Bold(); t.Span($" sur {b.AnzahlSchueler}"); });
            }));
            zeile.RelativeItem().Element(e => Kasten(e, "DÉCISION DE FIN D'ANNÉE", s =>
            {
                foreach (var text in new[] { "Admis(e) en classe supérieure", "Redouble la classe", "Exclu(e)" })
                    Ankreuzen(s, text, b.Entscheidung == text);
            }));
            zeile.RelativeItem().Element(e => Kasten(e, "LE PROFESSEUR PRINCIPAL", s => s.Item().Height(55)));
        });
    }

    private static string PeriodenWort(BulletinSnapshot b)
    {
        var titel = b.Titel.ToLowerInvariant();
        return titel.Contains("semestre") ? "SEMESTRIELLE" : titel.Contains("trimestre") ? "TRIMESTRIELLE" : "DE LA PÉRIODE";
    }
}
