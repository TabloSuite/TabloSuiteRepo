using System.Globalization;
using QuestPDF.Infrastructure;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Organisation.Druck;

/// <summary>PDFs des Bereichs Organisation (Zugangsliste, Schülerkarten, Stundenplan) und ihre Endpunkte.</summary>
public static class DruckRegistrierung
{
    public static IServiceCollection AddOrganisationDruck(this IServiceCollection services)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Ungewöhnliche Zeichen in Namen dürfen den Druck nicht abbrechen (Rückfallschrift statt Ausnahme).
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        services.AddScoped<KartenService>();
        services.AddScoped<DruckService>();
        return services;
    }

    public static WebApplication MapOrganisationDruckEndpunkte(this WebApplication app)
    {
        // Zugangsliste: nur der Ersteller des Stapels, solange er in der Ablage liegt (30 Minuten).
        app.MapGet("/direction/acces/{stapelId:guid}", (Guid stapelId, HttpContext http, DruckService druck, CancellationToken ct)
                => AusfuehrenAsync(http, () => druck.ZugangslisteAsync(Konto(http), stapelId, ct), keinZugriff: StatusCodes.Status404NotFound))
            .RequireAuthorization(Richtlinien.Direction);

        // Schülerkarten einer Klasse der eigenen Schule (fremde Klasse → 404).
        app.MapGet("/direction/cartes/{klasseId:guid}", (Guid klasseId, HttpContext http, DruckService druck, CancellationToken ct)
                => AusfuehrenAsync(http, () => druck.KartenAsync(Konto(http), klasseId, ct), keinZugriff: StatusCodes.Status404NotFound))
            .RequireAuthorization(Richtlinien.Direction);

        // Stundenplan einer Klasse: Direktion jeder Plan, sonst nur veröffentlicht und betroffen.
        app.MapGet("/emplois-du-temps/{planId:guid}/pdf", (Guid planId, HttpContext http, DruckService druck, CancellationToken ct)
                => AusfuehrenAsync(http, () => druck.PlanAsync(Konto(http), planId, ct), keinZugriff: StatusCodes.Status403Forbidden))
            .RequireAuthorization();

        // Woche einer Lehrkraft (?tag=yyyy-MM-dd, Standard heute).
        app.MapGet("/enseignant/emploi-du-temps/pdf", (string? tag, HttpContext http, DruckService druck, TimeProvider zeit, CancellationToken ct) =>
            {
                var datum = DateOnly.TryParseExact(tag, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : new SchulZeit(zeit).Heute;
                return AusfuehrenAsync(http, () => druck.LehrerWocheAsync(Konto(http), datum, ct), keinZugriff: StatusCodes.Status403Forbidden);
            })
            .RequireAuthorization(Richtlinien.Unterricht);
        return app;
    }

    private static Guid Konto(HttpContext http) => http.User.KontoId() ?? throw new KeinZugriffFehler();

    /// <summary>PDF inline ausliefern (nicht zwischenspeichern); fachliche Fehler als Text mit Status.</summary>
    private static async Task<IResult> AusfuehrenAsync(HttpContext http, Func<Task<PdfDatei>> erzeugen, int keinZugriff)
    {
        try
        {
            var datei = await erzeugen();
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers.ContentDisposition = $"inline; filename=\"{datei.Dateiname}\"";
            return Results.Bytes(datei.Inhalt, "application/pdf");
        }
        catch (KeinZugriffFehler k)
        {
            return Results.Text(keinZugriff == StatusCodes.Status404NotFound ? "Document introuvable." : k.Message, "text/plain; charset=utf-8", statusCode: keinZugriff);
        }
        catch (FachFehler f)
        {
            return Results.Text(f.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
        }
    }
}
