using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.DataModels.Unterricht;
using SkoulouApp.Core.Services.Storage;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.WebApp.Services;

/// <summary>Daten einer Schülerkarte.</summary>
public sealed record Schuelerkarte(string Name, string Klasse, string? Matrikel, string? Geburtsdatum, byte[]? Foto);

/// <summary>
/// Cartes d'élève einer Klasse als A4-PDF (10 Karten im Scheckkartenformat 85,6 × 54 mm, zum Ausschneiden).
/// Nur Direktion und Koordination der eigenen Schule; Fotos aus dem Profilbild, sonst ein Feld zum Aufkleben.
/// </summary>
public sealed class SchuelerkartenService(IDbContextFactory<ApplicationDbContext> factory, IBlobMediaLibraryService? medien = null)
{
    public async Task<(byte[] Pdf, string Dateiname)?> KlasseAsync(Guid benutzerID, Guid klasseID, CancellationToken token = default)
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var klasse = await db.Klassen.AsNoTracking().SingleOrDefaultAsync(k => k.Id == klasseID && !k.IstSystemNiveau, token);
        if (klasse?.SchoolId is not Guid schulID) return null;

        var rollen = await (from benutzerRolle in db.UserRoles.AsNoTracking()
                            join rolle in db.Roles.AsNoTracking() on benutzerRolle.RoleId equals rolle.Id
                            where benutzerRolle.UserId == benutzerID
                            select rolle.Name!).ToListAsync(token);
        var direktion = (rollen.Contains(Rollen.SchulLeiter) || rollen.Contains(Rollen.Koordinator))
            && await db.UserSchools.AsNoTracking().AnyAsync(l => l.AppUserId == benutzerID && l.SchoolId == schulID && l.CanManageStudents
                && !l.IstPlattformZuordnung && !l.AppUser.IsBlocked, token);
        if (!direktion) return null;

        var schule = await db.Schools.AsNoTracking().SingleAsync(s => s.Id == schulID, token);
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var schuljahr = await db.Set<Schuljahr>().AsNoTracking()
            .Where(j => j.SchulID == schulID && !j.IstGeschlossen && j.Beginn <= heute && j.Ende >= heute)
            .Select(j => j.Bezeichnung).FirstOrDefaultAsync(token);
        var logoID = await db.Set<BulletinVorlage>().AsNoTracking().Where(v => v.SchulID == schulID && v.LogoAssetID != null)
            .OrderByDescending(v => v.GeaendertAmUtc ?? v.ErstelltAmUtc).Select(v => v.LogoAssetID).FirstOrDefaultAsync(token);
        var logo = await BildAsync(logoID, benutzerID, rollen, token);

        var schueler = await db.StudentProfiles.AsNoTracking()
            .Where(p => p.SchoolId == schulID && p.CurrentKlasseId == klasseID && !p.IsArchived && !p.AppUser.IsBlocked)
            .OrderBy(p => p.AppUser.Nachname).ThenBy(p => p.AppUser.Vorname)
            .Select(p => new { p.AppUser.FullName, p.AppUser.Nachname, p.AppUser.Vorname, p.Matrikelnummer, p.AppUser.GeburtsdatumUtc, p.AppUser.ProfilBildAssetId })
            .ToListAsync(token);
        var karten = new List<Schuelerkarte>();
        foreach (var s in schueler)
        {
            var name = string.IsNullOrWhiteSpace(s.Nachname) ? s.FullName : $"{s.Nachname.ToUpperInvariant()} {s.Vorname}";
            karten.Add(new Schuelerkarte(name, klasse.Code, s.Matrikelnummer, s.GeburtsdatumUtc?.ToString("dd'/'MM'/'yyyy"),
                await BildAsync(s.ProfilBildAssetId, benutzerID, rollen, token)));
        }

        var pdf = Pdf(schule.OffizielleBezeichnung ?? schule.Name, schule.City, schuljahr, logo, karten);
        return (pdf, $"cartes-{klasse.Code}.pdf");
    }

    private async Task<byte[]?> BildAsync(Guid? assetID, Guid benutzerID, IReadOnlyCollection<string> rollen, CancellationToken token)
    {
        if (assetID is not Guid id || medien is null) return null;
        try
        {
            var download = await medien.OpenImageAsync(id, benutzerID, rollen, token);
            if (download is null || download.ContentType is not ("image/png" or "image/jpeg")) return null;
            await using var stream = download.Content;
            using var puffer = new MemoryStream();
            await stream.CopyToAsync(puffer, token);
            return puffer.Length <= 3 * 1024 * 1024 ? puffer.ToArray() : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null; // Fehlendes Bild blockiert die Karten nicht.
        }
    }

    public static byte[] Pdf(string schule, string? ort, string? schuljahr, byte[]? logo, IReadOnlyList<Schuelerkarte> karten)
        => Dokument(schule, ort, schuljahr, logo, karten).GeneratePdf();

    public static Document Dokument(string schule, string? ort, string? schuljahr, byte[]? logo, IReadOnlyList<Schuelerkarte> karten)
        => Document.Create(d => d.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(10, Unit.Millimetre);
            page.DefaultTextStyle(x => x.FontSize(7).FontColor("#111827"));
            page.Content().Column(spalte =>
            {
                if (karten.Count == 0) { spalte.Item().Text("Aucun élève actif dans cette classe.").FontSize(11); return; }
                foreach (var paar in karten.Chunk(2))
                {
                    spalte.Item().PaddingBottom(4, Unit.Millimetre).Row(zeile =>
                    {
                        zeile.Spacing(6, Unit.Millimetre);
                        foreach (var karte in paar)
                            zeile.ConstantItem(85.6f, Unit.Millimetre).Height(54f, Unit.Millimetre).Element(e => Karte(e, karte, schule, ort, schuljahr, logo));
                    });
                }
            });
        }));

    private static void Karte(IContainer container, Schuelerkarte k, string schule, string? ort, string? schuljahr, byte[]? logo)
    {
        container.Border(0.6f).BorderColor("#9ca3af").Column(karte =>
        {
            karte.Item().Background("#1e3a8a").PaddingHorizontal(6).PaddingVertical(4).Row(kopf =>
            {
                if (logo is { Length: > 0 }) kopf.ConstantItem(16).Height(16).Background(Colors.White).Image(logo).FitArea();
                kopf.RelativeItem().PaddingLeft(logo is { Length: > 0 } ? 5 : 0).Column(t =>
                {
                    t.Item().Text(schule).Bold().FontSize(8).FontColor(Colors.White);
                    t.Item().Text($"CARTE D'ÉLÈVE{(schuljahr is null ? "" : " · " + schuljahr)}").FontSize(6.5f).FontColor("#ccfbf1");
                });
            });
            karte.Item().Padding(6).Row(inhalt =>
            {
                inhalt.ConstantItem(20, Unit.Millimetre).Height(25, Unit.Millimetre).Border(0.5f).BorderColor("#9ca3af").AlignCenter().AlignMiddle().Element(foto =>
                {
                    if (k.Foto is { Length: > 0 }) foto.Image(k.Foto).FitArea();
                    else foto.Text("Photo").FontSize(6).FontColor("#9ca3af");
                });
                inhalt.RelativeItem().PaddingLeft(6).Column(daten =>
                {
                    daten.Spacing(1.5f);
                    daten.Item().Text(k.Name).Bold().FontSize(8.5f);
                    daten.Item().Text(t => { t.Span("Classe : "); t.Span(k.Klasse).SemiBold(); });
                    daten.Item().Text(t => { t.Span("Matricule : "); t.Span(k.Matrikel ?? "—").SemiBold(); });
                    if (k.Geburtsdatum is not null) daten.Item().Text($"Né(e) le : {k.Geburtsdatum}");
                });
                if (!string.IsNullOrWhiteSpace(k.Matrikel))
                {
                    using var code = QRCoder.QRCodeGenerator.GenerateQrCode($"SKOULOU:{k.Matrikel}", QRCoder.QRCodeGenerator.ECCLevel.M);
                    using var png = new QRCoder.PngByteQRCode(code);
                    var bild = png.GetGraphic(4);
                    inhalt.ConstantItem(14, Unit.Millimetre).AlignBottom().Image(bild);
                }
            });
            karte.Item().ExtendVertical().AlignBottom().Column(fuss =>
            {
                fuss.Item().PaddingHorizontal(6).PaddingBottom(2).Row(r =>
                {
                    r.RelativeItem().Text(schuljahr is null ? "" : $"Valable pour l'année scolaire {schuljahr}").FontSize(5.5f).FontColor("#6b7280");
                    r.RelativeItem().AlignRight().Text($"{(ort is null ? "" : ort + " · ")}Signature de la direction").FontSize(5.5f).FontColor("#6b7280");
                });
                fuss.Item().Height(2.2f).Background("#0d9488");
            });
        });
    }
}
