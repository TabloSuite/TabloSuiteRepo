using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Organisation.Import;

/// <summary>Downloads des Imports: leere Vorlage je Art und Fehlerliste einer Vorschau (beide XLSX).</summary>
public static class ImportEndpunkte
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static WebApplication MapImportEndpunkte(this WebApplication app)
    {
        app.MapGet("/direction/import/modele/{art}", async (string art, Guid? annee, HttpContext http, ImportService import, CancellationToken ct) =>
        {
            if (http.User.KontoId() is not { } kontoId) return Results.Unauthorized();
            if (!Enum.TryParse<ImportArt>(art, ignoreCase: true, out var importArt)) return Results.NotFound();
            try
            {
                var datei = await import.VorlageAsync(kontoId, importArt, annee, ct);
                return Results.File(datei, Xlsx, ImportVorlage.DateiName(importArt));
            }
            catch (KeinZugriffFehler)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (FachFehler f)
            {
                return Results.BadRequest(f.Message);
            }
        }).RequireAuthorization(Richtlinien.Direction);

        app.MapGet("/direction/import/erreurs/{id:guid}", async (Guid id, HttpContext http, ImportService import, CancellationToken ct) =>
        {
            if (http.User.KontoId() is not { } kontoId) return Results.Unauthorized();
            try
            {
                return Results.File(await import.FehlerDateiAsync(kontoId, id, ct), Xlsx, "erreurs-import.xlsx");
            }
            catch (KeinZugriffFehler)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (FachFehler f)
            {
                return Results.BadRequest(f.Message);
            }
        }).RequireAuthorization(Richtlinien.Direction);
        return app;
    }
}
