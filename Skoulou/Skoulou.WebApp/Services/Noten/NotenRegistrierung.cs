using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Organisation;
using Skoulou.WebApp.Services.Organisation.Druck;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Noten;

/// <summary>Erzeugt Bulletin-PDFs aus Snapshots (Bilder werden über ihre Medium-Referenz geladen und geprüft).</summary>
public sealed class BulletinPdfService(IDbContextFactory<SkoulouDbContext> dbFactory, IMedienSpeicher speicher, BulletinLeseService lesen,
    VorlagenService vorlagen, IOptions<AppOptionen> app)
{
    public async Task<PdfDatei> BulletinAsync(Guid akteurId, Guid bulletinId, string basisUrl, CancellationToken ct = default)
    {
        var d = await lesen.DruckAsync(akteurId, bulletinId, ct);
        return new PdfDatei(BulletinPdf.Erzeuge([await SeiteAsync(d, basisUrl, ct)]), d.Dateiname);
    }

    public async Task<PdfDatei> KlasseAsync(Guid akteurId, Guid klasseId, Guid periodeId, string basisUrl, CancellationToken ct = default)
    {
        var liste = await lesen.KlassenDruckAsync(akteurId, klasseId, periodeId, ct);
        var seiten = new List<BulletinSeite>();
        foreach (var d in liste) seiten.Add(await SeiteAsync(d, basisUrl, ct));
        var i = liste[0].Snapshot.Inhalt;
        return new PdfDatei(BulletinPdf.Erzeuge(seiten), $"bulletins-{DruckService.DateiTeil(i.Klasse)}-{DruckService.DateiTeil(i.Periode)}.pdf");
    }

    public async Task<PdfDatei> EntwurfAsync(Guid akteurId, Guid klasseId, Guid periodeId, Guid einschreibungId, CancellationToken ct = default)
    {
        var d = await lesen.EntwurfAsync(akteurId, klasseId, periodeId, einschreibungId, ct);
        return new PdfDatei(BulletinPdf.Erzeuge([await SeiteAsync(d, null, ct)]), d.Dateiname);
    }

    public async Task<PdfDatei> VorlageAsync(Guid akteurId, Guid? vorlageId, CancellationToken ct = default)
    {
        var d = await vorlagen.BeispielAsync(akteurId, vorlageId, ct);
        return new PdfDatei(BulletinPdf.Erzeuge([await SeiteAsync(d, null, ct)]), d.Dateiname);
    }

    private async Task<BulletinSeite> SeiteAsync(BulletinDruck d, string? basisUrl, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var logo = DruckStilBild(await BildDatei.LadeAsync(db, speicher, d.LogoMediumId, ct));
        var foto = d.Snapshot.Inhalt.Layout.ZeigeFoto ? DruckStilBild(await BildDatei.LadeAsync(db, speicher, d.FotoMediumId, ct)) : null;
        var basis = string.IsNullOrWhiteSpace(app.Value.BaseUrl) ? basisUrl : app.Value.BaseUrl;
        var url = d.Snapshot.Dokument?.Token is { } token && !string.IsNullOrWhiteSpace(basis) ? $"{basis!.TrimEnd('/')}/verification/{token}" : null;
        return new BulletinSeite(d.Snapshot, logo, foto, d.Status, url);
    }

    /// <summary>Nur Bilder, die QuestPDF lesen kann (sonst ohne Bild drucken statt abbrechen).</summary>
    private static byte[]? DruckStilBild(byte[]? bild)
    {
        if (bild is null || bild.Length == 0) return null;
        try
        {
            QuestPDF.Infrastructure.Image.FromBinaryData(bild);
            return bild;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}

/// <summary>Bereich Noten: Dienste und Endpunkte (PDF, Vorschau). Die alte Prüf-Adresse leitet E1 weiter (AuthEndpunkte).</summary>
public static class NotenRegistrierung
{
    public static IServiceCollection AddNoten(this IServiceCollection services)
    {
        services.AddScoped<NotenService>();
        services.AddScoped<BewertungService>();
        services.AddScoped<KlassenratService>();
        services.AddScoped<BulletinService>();
        services.AddScoped<BulletinLeseService>();
        services.AddScoped<VorlagenService>();
        services.AddScoped<NotenMails>();
        services.AddScoped<BulletinPdfService>();
        services.AddScoped<JahreswechselService>();
        return services;
    }

    public static WebApplication MapNotenEndpunkte(this WebApplication app)
    {
        // Ein Bulletin: Direktion, Professeur principal, Schüler/Eltern (nur gültige eigene). Fremd → 403.
        app.MapGet("/bulletins/{id:guid}/pdf", (Guid id, HttpContext http, BulletinPdfService pdf, CancellationToken ct)
            => PdfAsync(http, () => pdf.BulletinAsync(Konto(http), id, Basis(http), ct))).RequireAuthorization();

        // Alle gültigen Bulletins einer Klasse und Periode (Direktion, Professeur principal).
        app.MapGet("/bulletins/klasse/{klasse:guid}/{periode:guid}/pdf", (Guid klasse, Guid periode, HttpContext http, BulletinPdfService pdf, CancellationToken ct)
            => PdfAsync(http, () => pdf.KlasseAsync(Konto(http), klasse, periode, Basis(http), ct))).RequireAuthorization(Richtlinien.Unterricht);

        // Entwurf eines Schülers vor der Veröffentlichung (ohne Nummer und QR).
        app.MapGet("/bulletins/apercu/{klasse:guid}/{periode:guid}/{einschreibung:guid}",
                (Guid klasse, Guid periode, Guid einschreibung, HttpContext http, BulletinPdfService pdf, CancellationToken ct)
                    => PdfAsync(http, () => pdf.EntwurfAsync(Konto(http), klasse, periode, einschreibung, ct)))
            .RequireAuthorization(Richtlinien.Unterricht);

        // Vorschau einer Vorlage mit erfundenen Daten.
        app.MapGet("/bulletins/modeles/apercu", (Guid? vorlage, HttpContext http, BulletinPdfService pdf, CancellationToken ct)
            => PdfAsync(http, () => pdf.VorlageAsync(Konto(http), vorlage, ct))).RequireAuthorization(Richtlinien.Direction);

        return app;
    }

    private static Guid Konto(HttpContext http) => http.User.KontoId() ?? throw new KeinZugriffFehler();

    private static string Basis(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}";

    private static async Task<IResult> PdfAsync(HttpContext http, Func<Task<PdfDatei>> erzeugen)
    {
        try
        {
            var datei = await erzeugen();
            http.Response.Headers.CacheControl = "no-store, private";
            http.Response.Headers.ContentDisposition = $"inline; filename=\"{datei.Dateiname}\"";
            return Results.Bytes(datei.Inhalt, "application/pdf");
        }
        catch (KeinZugriffFehler k)
        {
            return Results.Text(k.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
        }
        catch (FachFehler f)
        {
            return Results.Text(f.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
