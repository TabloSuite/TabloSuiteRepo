using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Werkzeuge;

namespace Skoulou.WebApp.Services.Noten;

/// <summary>Ein Bulletin zum Drucken: Snapshot, geprüfte Bilder, Status und Prüf-Adresse (null = ohne QR).</summary>
public sealed record BulletinSeite(BulletinSnapshot Snapshot, byte[]? Logo, byte[]? Foto, BulletinStatus Status, string? PruefUrl);

/// <summary>
/// Bulletin als A4-PDF (QuestPDF) – allgemeines Layout der Alt-App (Fachkonzept 04 §3.19): dreispaltiger Kopf, Schülerblock,
/// Notentabelle, Kästen Assiduité / Moyenne / Résultats de la classe, Mentions + Appréciation du conseil, Bilan, Visa, QR.
/// Mehrere Bulletins (Klasse) = je Bulletin eine eigene Seite mit eigener Seitenzählung.
/// </summary>
public static class BulletinPdf
{
    private const string Rand = "#9aa7bd";
    private const string Zart = "#eef2f8";
    private const string Leise = "#56657f";
    private const string Text = "#0f1a33";
    private const string Rot = "#b91c1c";

    static BulletinPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

    public static byte[] Erzeuge(IReadOnlyList<BulletinSeite> seiten) => Dokument(seiten).GeneratePdf();

    public static IDocument Dokument(IReadOnlyList<BulletinSeite> seiten) => Document.Create(doc =>
    {
        foreach (var s in seiten) doc.Page(page => Seite(page, s));
    });

    private static void Seite(PageDescriptor page, BulletinSeite s)
    {
        var i = s.Snapshot.Inhalt;
        var l = i.Layout;
        var groesse = Math.Clamp(l.SchriftGroesse - 1, 7, 11);
        page.Size(PageSizes.A4);
        page.Margin(Math.Clamp(l.SeitenRand, 15, 40) * 0.55f, Unit.Millimetre);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontSize(groesse).FontColor(Text));
        if (s.Snapshot.Dokument?.Entwurf == true || s.Status != BulletinStatus.Publie)
            page.Foreground().AlignCenter().AlignMiddle().Rotate(-30).Text(s.Snapshot.Dokument?.Entwurf == true ? s.Snapshot.Dokument.Nummer
                : s.Status == BulletinStatus.Remplace ? "BULLETIN REMPLACÉ" : "BULLETIN RETIRÉ").FontSize(40).Bold().FontColor("#40dc2626");
        page.Header().Element(c => Kopf(c, s));
        page.Content().PaddingTop(2, Unit.Millimetre).Column(col =>
        {
            col.Spacing(2.2f, Unit.Millimetre);
            col.Item().Element(c => Titelband(c, i));
            col.Item().Element(c => SchuelerBlock(c, i, s.Foto));
            col.Item().Element(c => NotenTabelle(c, i));
            col.Item().Element(c => Ergebnisse(c, i));
            if (l.ZeigeJahresBilanz && i.Bilan is { } bilan) col.Item().Element(c => Bilan(c, i, bilan));
            col.Item().Element(c => Conseil(c, i));
            if (l.ZeigeUnterschriften) col.Item().Element(c => Unterschriften(c, s));
        });
        page.Footer().Element(c => Fuss(c, s));
    }

    private static void Kopf(IContainer c, BulletinSeite s)
    {
        var sch = s.Snapshot.Inhalt.Schule;
        var farbe = s.Snapshot.Inhalt.Layout.Farbe;
        c.PaddingBottom(1.5f, Unit.Millimetre).BorderBottom(1.2f).BorderColor(farbe).PaddingBottom(1.5f, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem(3).Column(col =>
            {
                if (sch.Ministerium is { } m) col.Item().Text(m.ToUpper(SkoulouKultur.Kultur)).FontSize(6.5f).SemiBold();
                if (sch.DirectionRegionale is { } d) col.Item().Text(d).FontSize(6.5f).FontColor(Leise);
                if (s.Logo is { } logo) col.Item().PaddingTop(1, Unit.Millimetre).Height(14, Unit.Millimetre).AlignLeft().Image(logo).FitHeight();
            });
            row.RelativeItem(4).AlignCenter().Column(col =>
            {
                col.Item().AlignCenter().Text(sch.Name).FontSize(12).Bold().FontColor(farbe);
                if (sch.Slogan is { } sl) col.Item().AlignCenter().Text(sl).FontSize(7).Italic().FontColor(Leise);
                if (sch.Adresse is { } ad) col.Item().AlignCenter().Text(ad).FontSize(7);
                if (sch.Kontakt is { } k) col.Item().AlignCenter().Text(k).FontSize(7).FontColor(Leise);
            });
            row.RelativeItem(3).AlignRight().Column(col =>
            {
                if (sch.Republik is { } r) col.Item().AlignRight().Text(r).FontSize(7).Bold();
                if (sch.Motto is { } mo) col.Item().AlignRight().Text(mo).FontSize(6.5f).Italic();
                col.Item().AlignRight().Text($"Année scolaire {s.Snapshot.Inhalt.Jahr}").FontSize(7).SemiBold();
                if (sch.Statut is { } st) col.Item().AlignRight().Text(st).FontSize(6.5f).FontColor(Leise);
                if (sch.Zulassung is { } z) col.Item().AlignRight().Text($"Autorisation : {z}").FontSize(6.5f).FontColor(Leise);
            });
        });
    }

    private static void Titelband(IContainer c, BulletinInhalt i)
        => c.Background(i.Layout.Farbe).PaddingVertical(1.6f, Unit.Millimetre).PaddingHorizontal(3, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem().Text(i.Titel).FontSize(10.5f).Bold().FontColor(Colors.White).LetterSpacing(0.04f);
            row.AutoItem().AlignMiddle().Text(i.Periode).FontSize(9.5f).SemiBold().FontColor(Colors.White);
        });

    private static void SchuelerBlock(IContainer c, BulletinInhalt i, byte[]? foto)
    {
        var s = i.Schueler;
        c.Border(0.6f).BorderColor(Rand).Padding(2, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Spacing(0.6f, Unit.Millimetre);
                col.Item().Text(t => { t.Span("Nom et prénoms : ").FontColor(Leise); t.Span(s.Name).Bold().FontSize(10); });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Matricule : ").FontColor(Leise); t.Span(s.Matrikel ?? "—").SemiBold(); });
                    r.RelativeItem().Text(t => { t.Span("Classe : ").FontColor(Leise); t.Span(i.Klasse).SemiBold(); t.Span($"   Effectif : {i.Effectif}").FontColor(Leise); });
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(t =>
                    {
                        t.Span("Né(e) le : ").FontColor(Leise);
                        t.Span(s.Geburtsdatum ?? "—").SemiBold();
                        if (s.Geburtsort is { } o) { t.Span(" à ").FontColor(Leise); t.Span(o).SemiBold(); }
                    });
                    r.RelativeItem().Text(t =>
                    {
                        t.Span("Sexe : ").FontColor(Leise); t.Span(s.Geschlecht ?? "—").SemiBold();
                        t.Span("   Redoublant(e) : ").FontColor(Leise); t.Span(s.Redoublant ? "oui" : "non").SemiBold();
                    });
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Nationalité : ").FontColor(Leise); t.Span(s.Nationalitaet ?? "—").SemiBold(); });
                    r.RelativeItem().Text(t =>
                    {
                        t.Span("Régime : ").FontColor(Leise); t.Span(s.Interne ? "interne" : "externe").SemiBold();
                        if (s.Affecte) t.Span(" · affecté(e)").SemiBold();
                    });
                });
            });
            if (i.Layout.ZeigeFoto && foto is not null)
                row.ConstantItem(18, Unit.Millimetre).Height(22, Unit.Millimetre).Border(0.4f).BorderColor(Rand).Image(foto).FitArea();
        });
    }

    private static void NotenTabelle(IContainer c, BulletinInhalt i)
    {
        var l = i.Layout;
        var bilan = l.ZeigeJahresBilanz && i.Bilan is not null;
        c.Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn(l.ZeigeLehrkraefte ? 3.2f : 2.6f);
                if (l.ZeigeTeilnoten) cd.RelativeColumn(1.9f);
                cd.RelativeColumn(1f);
                if (l.ZeigeKoeffizienten) cd.RelativeColumn(0.7f);
                if (l.ZeigeKoeffizienten && l.ZeigePunkte) cd.RelativeColumn(1f);
                if (l.ZeigeFachRang) cd.RelativeColumn(0.8f);
                if (bilan) cd.RelativeColumn(1f);
                if (l.ZeigeAppreciation) cd.RelativeColumn(2f);
                if (l.ZeigeSignaturSpalte) cd.RelativeColumn(1.1f);
            });
            t.Header(h =>
            {
                void K(string text) => h.Cell().Background(Zart).Border(0.5f).BorderColor(Rand).Padding(1.2f, Unit.Millimetre).AlignCenter().Text(text).FontSize(6.8f).Bold();
                K("DISCIPLINES");
                if (l.ZeigeTeilnoten) K("NOTES /20");
                K("MOY./20");
                if (l.ZeigeKoeffizienten) K("COEF");
                if (l.ZeigeKoeffizienten && l.ZeigePunkte) K("MOY × COEF");
                if (l.ZeigeFachRang) K("RANG");
                if (bilan) K("MOY. ANN.");
                if (l.ZeigeAppreciation) K("APPRÉCIATIONS");
                if (l.ZeigeSignaturSpalte) K("SIGNATURE");
            });
            var gruppen = l.ZeigeFachGruppen ? i.Faecher.GroupBy(f => f.Gruppe ?? string.Empty).ToList() : [i.Faecher.GroupBy(_ => string.Empty).First()];
            if (i.Faecher.Count == 0) gruppen = [];
            foreach (var g in gruppen)
            {
                foreach (var f in g) Zeile(t, l, bilan, f);
                if (l.ZeigeFachGruppen && g.Key.Length > 0 && i.Gruppen.FirstOrDefault(x => x.Name == g.Key) is { } gr)
                {
                    t.Cell().ColumnSpan((uint)Spalten(l, bilan)).Background(Zart).Border(0.5f).BorderColor(Rand).PaddingHorizontal(1.5f, Unit.Millimetre).PaddingVertical(0.6f, Unit.Millimetre)
                        .Text($"Bilan {g.Key} : {gr.Moyenne} (coef. {gr.Koeffizienten:0.##})").FontSize(6.8f).SemiBold().Italic();
                }
            }
            if (l.ZeigeKoeffizienten && l.ZeigePunkte)
            {
                void Z(string text) => t.Cell().Background(Zart).Border(0.5f).BorderColor(Rand).Padding(1.2f, Unit.Millimetre).AlignCenter().Text(text).FontSize(7.2f).Bold();
                Z("TOTAUX");
                if (l.ZeigeTeilnoten) Z(string.Empty);
                Z(string.Empty);
                Z(i.SummeKoeffizienten.ToString("0.##", SkoulouKultur.Kultur));
                Z(i.SummePunkte is { } sp ? Zahl(sp) : "—");
                for (var k = 0; k < Spalten(l, bilan) - (l.ZeigeTeilnoten ? 5 : 4); k++) Z(string.Empty);
            }
        });
    }

    private static int Spalten(SnapshotLayout l, bool bilan)
        => 2 + (l.ZeigeTeilnoten ? 1 : 0) + (l.ZeigeKoeffizienten ? 1 : 0) + (l.ZeigeKoeffizienten && l.ZeigePunkte ? 1 : 0) + (l.ZeigeFachRang ? 1 : 0)
           + (bilan ? 1 : 0) + (l.ZeigeAppreciation ? 1 : 0) + (l.ZeigeSignaturSpalte ? 1 : 0);

    private static void Zeile(TableDescriptor t, SnapshotLayout l, bool bilan, SnapshotFach f)
    {
        IContainer Zelle() => t.Cell().Border(0.5f).BorderColor(Rand).PaddingHorizontal(1.2f, Unit.Millimetre).PaddingVertical(0.8f, Unit.Millimetre);
        Zelle().Column(col =>
        {
            col.Item().Text(f.Name).SemiBold();
            if (l.ZeigeLehrkraefte && f.Lehrer is { } le) col.Item().Text(le).FontSize(6.3f).FontColor(Leise);
        });
        if (l.ZeigeTeilnoten) Zelle().AlignCenter().AlignMiddle().Text(f.Teilnoten ?? "").FontSize(6.6f).FontColor(Leise);
        Zelle().AlignCenter().AlignMiddle().Text(f.MoyenneText).Bold();
        if (l.ZeigeKoeffizienten) Zelle().AlignCenter().AlignMiddle().Text(f.Koeffizient.ToString("0.##", SkoulouKultur.Kultur));
        if (l.ZeigeKoeffizienten && l.ZeigePunkte) Zelle().AlignCenter().AlignMiddle().Text(f.Punkte is { } p ? Zahl(p) : "—");
        if (l.ZeigeFachRang) Zelle().AlignCenter().AlignMiddle().Text(f.Rang ?? "—");
        if (bilan) Zelle().AlignCenter().AlignMiddle().Text(f.JahresMoyenne ?? "—");
        if (l.ZeigeAppreciation) Zelle().AlignMiddle().Text(f.Appreciation ?? "").FontSize(6.8f).Italic();
        if (l.ZeigeSignaturSpalte) Zelle().Text(string.Empty);
    }

    private static void Kasten(IContainer c, string titel, string farbe, Action<ColumnDescriptor> inhalt)
        => c.Border(0.6f).BorderColor(Rand).Column(col =>
        {
            col.Item().Background(Zart).PaddingHorizontal(2, Unit.Millimetre).PaddingVertical(0.8f, Unit.Millimetre).Text(titel).FontSize(6.8f).Bold().FontColor(farbe);
            col.Item().Padding(2, Unit.Millimetre).Column(inhalt);
        });

    private static void Ergebnisse(IContainer c, BulletinInhalt i)
    {
        var l = i.Layout;
        c.Row(row =>
        {
            row.Spacing(2, Unit.Millimetre);
            if (l.ZeigeAnwesenheit)
                row.RelativeItem().Element(e => Kasten(e, "ASSIDUITÉ", l.Farbe, col =>
                {
                    if (!i.Anwesenheit.Erfasst) { col.Item().Text("Présences non renseignées.").FontColor(Leise); return; }
                    col.Item().Text($"Absences : {i.Anwesenheit.Absences} jour(s)");
                    col.Item().Text($"dont justifiées : {i.Anwesenheit.Justifiees}").FontColor(Leise);
                    col.Item().Text($"Retards : {i.Anwesenheit.Retards} ({i.Anwesenheit.Minuten} min)");
                }));
            row.RelativeItem(1.3f).Element(e => Kasten(e, $"MOYENNE DU {i.Periode.ToUpper(SkoulouKultur.Kultur)}", l.Farbe, col =>
            {
                col.Item().AlignCenter().Text(i.MoyenneText + (i.Moyenne is null ? "" : " / 20")).FontSize(15).Bold().FontColor(l.Farbe);
                if (l.ZeigeNoteInWorten && i.MoyenneInWorten is { } w) col.Item().AlignCenter().Text($"({w})").FontSize(6.8f).Italic();
                if (l.ZeigeRang) col.Item().AlignCenter().Text($"Rang : {i.Rang} sur {i.Effectif}").SemiBold();
                if (i.Appreciation is { } ap) col.Item().AlignCenter().Text(ap).Italic().FontColor(Leise);
                if (l.ZeigeVorperioden && i.Bilan is null && i.Vorperioden.Count > 0)
                    col.Item().PaddingTop(1, Unit.Millimetre).AlignCenter().Text(string.Join("   ", i.Vorperioden.Select(v => $"{v.Titel} : {v.Moyenne} ({v.Rang})"))).FontSize(6.5f).FontColor(Leise);
            }));
            if (l.ZeigeKlassenStatistik)
                row.RelativeItem().Element(e => Kasten(e, "RÉSULTATS DE LA CLASSE", l.Farbe, col =>
                {
                    if (i.Statistik is not { } st) { col.Item().Text("Pas encore de moyennes complètes.").FontColor(Leise); return; }
                    col.Item().Text($"Moyenne de la classe : {st.Mittel}");
                    col.Item().Text($"Plus forte : {st.Max}   Plus faible : {st.Min}");
                    col.Item().Text($"Moyenne ≥ 10 : {st.ProzentUeber10} %").FontColor(Leise);
                }));
        });
    }

    private static void Bilan(IContainer c, BulletinInhalt i, SnapshotBilan bilan)
        => Kasten(c, "BILAN DE FIN D'ANNÉE", i.Layout.Farbe, col => col.Item().Row(r =>
        {
            r.RelativeItem(2).Text(string.Join("   ", bilan.Perioden.Select(p => $"{p.Titel} : {p.Moyenne} ({p.Rang})"))).FontSize(7);
            r.RelativeItem().AlignRight().Column(cc =>
            {
                cc.Item().AlignRight().Text(t => { t.Span("Moyenne annuelle : "); t.Span(bilan.Moyenne).Bold(); });
                if (i.Layout.ZeigeRang) cc.Item().AlignRight().Text($"Rang annuel : {bilan.Rang}");
                cc.Item().AlignRight().Text(t => { t.Span("Décision : ").FontColor(Leise); t.Span(bilan.Entscheidung).Bold(); });
            });
        }));

    private static void Conseil(IContainer c, BulletinInhalt i)
    {
        var l = i.Layout;
        c.Row(row =>
        {
            row.Spacing(2, Unit.Millimetre);
            if (l.ZeigeMentionen)
                row.RelativeItem().Element(e => Kasten(e, "MENTIONS DU CONSEIL DE CLASSE", l.Farbe, col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(cc =>
                        {
                            Kreuz(cc, "Tableau d'honneur", i.Distinktion >= Distinktion.TableauHonneur);
                            Kreuz(cc, "Encouragements", i.Distinktion == Distinktion.Encouragements);
                            Kreuz(cc, "Félicitations", i.Distinktion == Distinktion.Felicitations);
                        });
                        r.RelativeItem().Column(cc =>
                        {
                            Kreuz(cc, "Avertissement travail", i.Sanktionen.HasFlag(Sanktionen.AvertissementTravail));
                            Kreuz(cc, "Avertissement conduite", i.Sanktionen.HasFlag(Sanktionen.AvertissementConduite));
                            Kreuz(cc, "Blâme travail", i.Sanktionen.HasFlag(Sanktionen.BlameTravail));
                            Kreuz(cc, "Blâme conduite", i.Sanktionen.HasFlag(Sanktionen.BlameConduite));
                        });
                    });
                }));
            row.RelativeItem().Element(e => Kasten(e, "APPRÉCIATION DU CONSEIL DE CLASSE", l.Farbe, col =>
                col.Item().MinHeight(12, Unit.Millimetre).Text(i.ConseilBemerkung ?? string.Empty).Italic()));
        });
    }

    private static void Kreuz(ColumnDescriptor col, string text, bool an)
        => col.Item().Text(t =>
        {
            t.Span(an ? "☒ " : "☐ ").FontSize(8.5f);
            t.Span(text).FontSize(7).FontColor(an ? Text : Leise);
        });

    private static void Unterschriften(IContainer c, BulletinSeite s)
    {
        var i = s.Snapshot.Inhalt;
        var datum = s.Snapshot.Dokument?.Datum ?? string.Empty;
        c.Row(row =>
        {
            row.Spacing(2, Unit.Millimetre);
            row.RelativeItem().Height(20, Unit.Millimetre).Element(e => Kasten(e, "LE PROFESSEUR PRINCIPAL", i.Layout.Farbe, col =>
                col.Item().Text(i.Klassenlehrer ?? string.Empty).FontSize(7)));
            row.RelativeItem(1.3f).Height(20, Unit.Millimetre).Element(e => Kasten(e, "VISA DU CHEF D'ÉTABLISSEMENT", i.Layout.Farbe, col =>
                col.Item().Text($"Fait à {i.Schule.Ort}, le {datum}").FontSize(7)));
            row.RelativeItem().Height(20, Unit.Millimetre).Element(e => Kasten(e, "SIGNATURE DES PARENTS", i.Layout.Farbe, _ => { }));
        });
    }

    private static void Fuss(IContainer c, BulletinSeite s)
    {
        var d = s.Snapshot.Dokument;
        var i = s.Snapshot.Inhalt;
        c.PaddingTop(1.5f, Unit.Millimetre).BorderTop(0.5f).BorderColor(Rand).PaddingTop(1.2f, Unit.Millimetre).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                if (d?.IstRectificatif == true)
                    col.Item().Text($"BULLETIN RECTIFICATIF — remplace le n° {d.ErsetztNummer}{(d.Grund is { } g ? $" ({g})" : "")}").FontSize(6.8f).Bold().FontColor(Rot);
                if (i.Layout.Fusstext is { } f) col.Item().Text(f).FontSize(6.5f).FontColor(Leise);
                col.Item().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(6.5f).FontColor(Leise));
                    t.Span($"N° {d?.Nummer ?? "—"} · délivré le {d?.Datum ?? "—"}");
                    if (s.PruefUrl is not null) t.Span(" · authenticité vérifiable avec le QR code");
                });
            });
            if (s.PruefUrl is { } url) row.ConstantItem(15, Unit.Millimetre).Height(15, Unit.Millimetre).Image(Qr(url)).FitArea();
        });
    }

    private static string Zahl(decimal wert) => Core.Noten.NotenTexte.Zahl(wert);

    private static byte[] Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var daten = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(daten).GetGraphic(8);
    }
}
