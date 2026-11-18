using System.Globalization;
using Skoulou.Core.Fehler;
using Skoulou.Core.Werkzeuge;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Organisation.Druck;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Finanzen;

/// <summary>Bereich Finanzen: Dienste, Hintergrunddienst der Plattformabrechnung und geprüfte Dokument-Endpunkte.</summary>
public static class FinanzenRegistrierung
{
    public static IServiceCollection AddFinanzen(this IServiceCollection services)
    {
        services.AddScoped<FinanzMails>();
        services.AddScoped<GebuehrenService>();
        services.AddScoped<ZahlungService>();
        services.AddScoped<QuittungService>();
        services.AddScoped<JournalService>();
        services.AddScoped<BerichtService>();
        services.AddScoped<PlattformAbrechnung>();
        services.AddScoped<RechnungService>();
        services.AddScoped<EssaiService>();
        services.AddScoped<AnteilService>();
        services.AddScoped<TarifService>();
        services.AddScoped<FamilienFinanzService>();
        services.AddHostedService<RechnungsDienst>();
        return services;
    }

    public static WebApplication MapFinanzenEndpunkte(this WebApplication app)
    {
        // Quittung: Personal mit Finanzrecht, der Schüler selbst, seine Eltern, Responsable/Admin (Prüfung im Dienst).
        app.MapGet("/finances/recu/{zahlungId:guid}", (Guid zahlungId, HttpContext http, QuittungService dienst, CancellationToken ct)
            => PdfAsync(http, () => dienst.PdfAsync(Konto(http), zahlungId, ct))).RequireAuthorization();

        // Plattformrechnung: Schule mit Recht, Responsable (seine Schulen), Admin.
        app.MapGet("/finances/facture/{rechnungId:guid}", (Guid rechnungId, HttpContext http, RechnungService dienst, CancellationToken ct)
            => PdfAsync(http, () => dienst.PdfAsync(Konto(http), rechnungId, ct))).RequireAuthorization();

        // Finanzbericht (?von=yyyy-MM-dd&bis=yyyy-MM-dd, Standard: laufendes Schuljahr bis heute).
        app.MapGet("/finances/rapport/pdf", async (string? von, string? bis, HttpContext http, BerichtService dienst, CancellationToken ct) =>
        {
            var konto = Konto(http);
            return await PdfAsync(http, async () =>
            {
                var standard = await dienst.StandardZeitraumAsync(konto, ct);
                return await dienst.PdfAsync(konto, Datum(von) ?? standard.Von, Datum(bis) ?? standard.Bis, ct);
            });
        }).RequireAuthorization(Richtlinien.Finanzen);

        // Beleg einer Buchung (inline, nicht zwischenspeichern).
        app.MapGet("/finances/justificatif/{buchungId:guid}", async (Guid buchungId, HttpContext http, JournalService dienst, CancellationToken ct) =>
        {
            try
            {
                var beleg = await dienst.BelegAsync(Konto(http), buchungId, ct);
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers.ContentDisposition = $"inline; filename=\"{Path.GetFileName(beleg.Dateiname).Replace("\"", string.Empty)}\"";
                return Results.Stream(beleg.Inhalt, beleg.ContentType);
            }
            catch (KeinZugriffFehler k)
            {
                return Results.Text(k.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
            }
            catch (FachFehler f)
            {
                return Results.Text(f.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
            }
        }).RequireAuthorization();
        return app;
    }

    private static Guid Konto(HttpContext http) => http.User.KontoId() ?? throw new KeinZugriffFehler();

    private static DateOnly? Datum(string? text)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>PDF inline ausliefern; fehlendes Recht → 403, fachlicher Fehler (z. B. unbekannt) → 404.</summary>
    private static async Task<IResult> PdfAsync(HttpContext http, Func<Task<PdfDatei>> erzeugen)
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
            return Results.Text(k.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status403Forbidden);
        }
        catch (FachFehler f)
        {
            return Results.Text(f.Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status404NotFound);
        }
    }
}
