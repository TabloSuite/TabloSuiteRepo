using Microsoft.Net.Http.Headers;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Fehler;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Infrastruktur.Vorlesen;

/// <summary>
/// Auslieferung der vorgelesenen Audio-Aufgaben (<c>/vorlesen/{schluessel}</c>): nur angemeldet, nur aus der Ablage der
/// eigenen Schule (fremde Schlüssel ergeben 404), Range-fähig, je Konto begrenzt. Der Schlüssel ist ein Hash – der Text
/// der Aufgabe (z. B. ein Diktat) steht nie in der Adresse.
/// </summary>
public static class VorleseEndpunkt
{
    public static IEndpointRouteBuilder MapVorleseEndpunkt(this IEndpointRouteBuilder app)
    {
        app.MapGet(VorleseRegeln.Pfadanfang + "{schluessel}", AusliefernAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Vorlesen);
        return app;
    }

    public static async Task<IResult> AusliefernAsync(string schluessel, HttpContext http, Zugriff zugriff, IMedienSpeicher speicher,
        CancellationToken ct)
    {
        if (http.User.KontoId() is not { } kontoId) return Results.Unauthorized();
        if (!VorleseRegeln.IstSchluessel(schluessel)) return Results.NotFound();

        Akteur akteur;
        try
        {
            akteur = await zugriff.LadeAkteurAsync(kontoId, ct);
        }
        catch (KeinZugriffFehler)
        {
            return Results.NotFound();
        }
        if (akteur.Status != KontoStatus.Actif) return Results.NotFound();
        if (Rollen.IstSchulRolle(akteur.Rolle) && akteur.SchulStatus != SchulStatus.Active) return Results.NotFound();

        var strom = await speicher.OeffnenAsync(VorleseRegeln.Pfad(akteur.SchuleId, schluessel), ct);
        if (strom is null) return Results.NotFound();
        // Inhalt hängt nur vom Schlüssel ab (Hash) – darf einen Tag im Browser bleiben.
        http.Response.Headers.CacheControl = "private, max-age=86400";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Stream(strom, VorleseRegeln.ContentType, entityTag: new EntityTagHeaderValue($"\"{schluessel}\""),
            enableRangeProcessing: strom.CanSeek);
    }
}
