using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Text;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

/// <summary>Daten eines Reçu für eine Zahlung der Schulgebühren (Snapshot zum Druckzeitpunkt).</summary>
public sealed record SchulgeldQuittung(string Nummer, string Schule, string? Adresse, string? Kontakt, string? Ort,
    string Schueler, string? Matrikel, string Klasse, DateTime Datum, decimal Betrag, string Zahlungsart,
    decimal Schulgeld, decimal BisherBezahlt, decimal Rest, string? Bemerkung, string ErfasstVon);

/// <summary>
/// Reçu de paiement für Schulgebühren (Raten oder Gesamtzahlung). Abrufen dürfen die Direktion,
/// Koordinatoren mit Finanzrecht der Schule, SuperAdmin/ProduktOwner (lesend) und der Schüler selbst.
/// </summary>
public sealed class QuittungService(IDbContextFactory<ApplicationDbContext> factory)
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public async Task<SchulgeldQuittung?> LadenAsync(Guid zahlungID, Guid benutzerID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var zahlung = await db.Set<RatenZahlung>().AsNoTracking()
            .Include(z => z.Durchlauf).ThenInclude(d => d.Klasse)
            .Include(z => z.Student)
            .Include(z => z.ErfasstVon)
            .SingleOrDefaultAsync(z => z.Id == zahlungID, token);
        if (zahlung is null) return null;

        var schulID = zahlung.Durchlauf.SchulID;
        if (zahlung.StudentId != benutzerID)
        {
            var zugriff = await new FinanzService(db).GetFinanceAccessAsync(benutzerID, schulID, cancellationToken: token);
            if (!zugriff.Menu || !(zugriff.Cycles || zugriff.Dashboard)) return null;
        }

        var schule = await db.Schools.AsNoTracking().SingleAsync(s => s.Id == schulID, token);
        var matrikel = await db.StudentProfiles.AsNoTracking().Where(p => p.AppUserId == zahlung.StudentId && p.SchoolId == schulID)
            .Select(p => p.Matrikelnummer).FirstOrDefaultAsync(token);
        // Stand bis einschließlich dieser Zahlung (gleiche Reihenfolge wie in der Zahlungsliste).
        var zahlungen = await db.Set<RatenZahlung>().AsNoTracking()
            .Where(z => z.DurchlaufID == zahlung.DurchlaufID && z.StudentId == zahlung.StudentId)
            .Select(z => new { z.Id, z.Betrag, z.Wirkungsdatum, z.ErstelltAmUtc })
            .ToListAsync(token);
        var bisher = zahlungen.Where(z => z.Wirkungsdatum < zahlung.Wirkungsdatum
                || (z.Wirkungsdatum == zahlung.Wirkungsdatum && (z.ErstelltAmUtc < zahlung.ErstelltAmUtc || z.Id == zahlung.Id)))
            .Sum(z => z.Betrag);
        var kontakt = string.Join(" · ", new[] { schule.DokumentTelefon, schule.DokumentEmail }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var bemerkung = string.IsNullOrWhiteSpace(zahlung.BeschreibungHtml) ? null : HtmlSanitizer.Sanitize(zahlung.BeschreibungHtml);
        bemerkung = bemerkung is null ? null : System.Text.RegularExpressions.Regex.Replace(bemerkung, "<[^>]+>", " ").Trim();

        return new SchulgeldQuittung(
            $"REC-{zahlung.Wirkungsdatum:yyyy}-{zahlung.Id.ToString("N")[..8].ToUpperInvariant()}",
            schule.OffizielleBezeichnung ?? schule.Name, schule.DokumentAdresse, kontakt.Length > 0 ? kontakt : null, schule.City,
            zahlung.Student.FullName, matrikel, zahlung.Durchlauf.Klasse.Title is { Length: > 0 } titel ? $"{zahlung.Durchlauf.Klasse.Code} · {titel}" : zahlung.Durchlauf.Klasse.Code,
            zahlung.Wirkungsdatum, zahlung.Betrag,
            zahlung.ZahlungsArt == ZahlungsArt.Cash ? "Paiement intégral" : "Versement (paiement échelonné)",
            zahlung.Durchlauf.NiveauPreis, bisher, Math.Max(0m, zahlung.Durchlauf.NiveauPreis - bisher),
            string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung, zahlung.ErfasstVon?.FullName ?? "");
    }

    public byte[] Pdf(SchulgeldQuittung q) => Dokument(q).GeneratePdf();

    public Document Dokument(SchulgeldQuittung q)
        => Document.Create(d => d.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(22);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor("#111827"));
            page.Content().AlignTop().Border(1).BorderColor("#4b5563").Padding(12).Column(c =>
            {
                c.Spacing(6);
                c.Item().Row(r =>
                {
                    r.RelativeItem().Column(s =>
                    {
                        s.Item().Text(q.Schule).Bold().FontSize(12).FontColor("#1e3a8a");
                        if (q.Adresse is not null) s.Item().Text(q.Adresse).FontSize(8);
                        if (q.Kontakt is not null) s.Item().Text(q.Kontakt).FontSize(8);
                    });
                    r.ConstantItem(170).AlignRight().Column(s =>
                    {
                        s.Item().AlignRight().Text("REÇU DE PAIEMENT").Bold().FontSize(13);
                        s.Item().AlignRight().Text($"N° {q.Nummer}").FontSize(8.5f);
                        s.Item().AlignRight().Text($"Date : {q.Datum:dd'/'MM'/'yyyy}").FontSize(8.5f);
                    });
                });
                c.Item().LineHorizontal(0.8f).LineColor("#9ca3af");
                c.Item().Text(t => { t.Span("Reçu de : "); t.Span(q.Schueler).Bold(); if (q.Matrikel is not null) t.Span($"   ·   Matricule : {q.Matrikel}"); });
                c.Item().Text($"Classe : {q.Klasse}");
                c.Item().Text($"Objet : frais de scolarité — {q.Zahlungsart}");
                c.Item().Background("#f3f4f6").Padding(8).Column(s =>
                {
                    s.Item().Text(t => { t.Span("Montant reçu : "); t.Span($"{Betrag(q.Betrag)}").Bold().FontSize(14); });
                    s.Item().Text($"Arrêté le présent reçu à la somme de {BetragInWorten(q.Betrag)} francs CFA.").Italic().FontSize(8.5f);
                });
                c.Item().Row(r =>
                {
                    r.RelativeItem().Text($"Frais de scolarité : {Betrag(q.Schulgeld)}");
                    r.RelativeItem().Text($"Total versé : {Betrag(q.BisherBezahlt)}");
                    r.RelativeItem().Text(t => { t.Span("Reste à payer : "); t.Span(Betrag(q.Rest)).Bold(); });
                });
                if (q.Bemerkung is not null) c.Item().Text($"Observation : {q.Bemerkung}").FontSize(8.5f);
                c.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text($"Enregistré par : {q.ErfasstVon}").FontSize(8);
                    r.RelativeItem().AlignRight().Column(s =>
                    {
                        s.Item().AlignRight().Text($"Fait à {q.Ort ?? "…………"}, le {q.Datum:dd'/'MM'/'yyyy}").FontSize(8.5f);
                        s.Item().AlignRight().Text("Signature et cachet").FontSize(8).FontColor("#6b7280");
                        s.Item().Height(36);
                    });
                });
            });
        }));

    private static string Betrag(decimal wert) => $"{wert.ToString("N0", Fr)} FCFA";

    /// <summary>Ganzzahliger Betrag in Worten (bis 999 999 999), z. B. 25 000 → « vingt-cinq mille ».</summary>
    public static string BetragInWorten(decimal betrag)
    {
        var n = (long)Math.Round(betrag, MidpointRounding.AwayFromZero);
        if (n == 0) return "zéro";
        var teile = new List<string>();
        var millionen = n / 1_000_000; var tausend = n / 1000 % 1000; var rest = n % 1000;
        if (millionen > 0) teile.Add(millionen == 1 ? "un million" : $"{Hundert((int)millionen)} millions");
        if (tausend > 0) teile.Add(tausend == 1 ? "mille" : $"{Hundert((int)tausend, ohnePlural: true)} mille");
        if (rest > 0) teile.Add(Hundert((int)rest));
        return string.Join(" ", teile);
    }

    private static readonly string[] Einer = ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf", "dix",
        "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept", "dix-huit", "dix-neuf"];
    private static readonly string[] Zehner = ["", "", "vingt", "trente", "quarante", "cinquante", "soixante", "soixante", "quatre-vingt", "quatre-vingt"];

    private static string Hundert(int n, bool ohnePlural = false)
    {
        var h = n / 100; var r = n % 100;
        var hundert = h switch { 0 => "", 1 => "cent", _ => Einer[h] + (r == 0 && !ohnePlural ? " cents" : " cent") };
        var unten = r == 0 ? "" : Unter100(r, ohnePlural);
        return string.Join(" ", new[] { hundert, unten }.Where(x => x.Length > 0));
    }

    private static string Unter100(int n, bool ohnePlural)
    {
        if (n < 20) return Einer[n];
        var z = n / 10; var e = n % 10;
        if (z is 7 or 9) e += 10;
        if (e == 0) return z == 8 && !ohnePlural ? "quatre-vingts" : Zehner[z];
        var verbindung = e is 1 or 11 && z != 8 && z != 9 ? " et " : "-";
        return Zehner[z] + verbindung + Einer[e];
    }
}
