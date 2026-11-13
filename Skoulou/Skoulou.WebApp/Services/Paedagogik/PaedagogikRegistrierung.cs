using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Medien;
using Skoulou.WebApp.Services.Plattform;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>Bereich Pädagogik: Dienste, Hintergrunddienst und Upload-Endpunkte.</summary>
public static class PaedagogikRegistrierung
{
    /// <summary>Kopfzeile, die jeder Upload mitschicken muss (fremde Seiten können sie ohne CORS nicht setzen).</summary>
    public const string UploadKopf = "X-Skoulou-Upload";

    public static IServiceCollection AddPaedagogik(this IServiceCollection services)
    {
        services.AddSingleton<InhaltBereiniger>();
        services.AddSingleton<AufgabenAbgleich>();
        services.AddSingleton<InhaltService>();
        services.AddSingleton<InhaltLeseService>();
        services.AddSingleton<ZuweisungService>();
        services.AddSingleton<LernService>();
        services.AddSingleton<PruefungsService>();
        services.AddSingleton<KorrekturService>();
        services.AddSingleton<UebersichtService>();
        services.AddSingleton<FormelService>();
        services.AddSingleton<MedienService>();
        services.AddSingleton<IMedienFreigabe, PaedagogikMedienFreigabe>();
        services.AddScoped<Components.Pages.Paedagogik.PaedagogikModul>();
        services.AddHostedService<PruefungsAbgabeDienst>();
        return services;
    }

    public sealed record UploadStart(MedienArt Art, string? Name, long Groesse);

    /// <summary>Hochladen in Teilen (je höchstens 4 MB): Start → Teile (PUT mit Offset) → Abschluss.</summary>
    public static WebApplication MapPaedagogikEndpunkte(this WebApplication app)
    {
        var gruppe = app.MapGroup("/api/paedagogik/medien").RequireAuthorization(Richtlinien.Unterricht).DisableAntiforgery();

        gruppe.MapPost("/start", (UploadStart start, HttpContext http, MedienService medien, CancellationToken ct)
            => Ausfuehren(http, async konto => Results.Ok(new { id = await medien.UploadStartenAsync(konto, start.Art, start.Name, start.Groesse, ct) })));

        gruppe.MapPut("/{id:guid}", (Guid id, long offset, HttpContext http, MedienService medien, CancellationToken ct)
            => Ausfuehren(http, async konto => Results.Ok(new { empfangen = await medien.TeilSchreibenAsync(konto, id, offset, http.Request.Body, ct) })));

        gruppe.MapPost("/{id:guid}/fin", (Guid id, HttpContext http, MedienService medien, CancellationToken ct)
            => Ausfuehren(http, async konto => Results.Ok(await medien.UploadAbschliessenAsync(konto, id, ct))));
        return app;
    }

    private static async Task<IResult> Ausfuehren(HttpContext http, Func<Guid, Task<IResult>> aktion)
    {
        if (!http.Request.Headers.ContainsKey(UploadKopf)) return Results.BadRequest(new { meldung = "Requête refusée." });
        if (http.User.KontoId() is not { } konto) return Results.Unauthorized();
        try
        {
            return await aktion(konto);
        }
        catch (FachFehler f)
        {
            return Results.BadRequest(new { meldung = f.Message });
        }
        catch (KeinZugriffFehler k)
        {
            return Results.Json(new { meldung = k.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}
