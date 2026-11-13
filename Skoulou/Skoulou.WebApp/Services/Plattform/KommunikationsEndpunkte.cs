using Microsoft.Extensions.Options;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>Kontaktformular (öffentlich, Honeypot, Rate-Limit) und Postmark-Webhook für eingehende Mails.</summary>
public static class KommunikationsEndpunkte
{
    public const long MaxWebhookBytes = 25 * 1024 * 1024;

    public static WebApplication MapKommunikationsEndpunkte(this WebApplication app)
    {
        app.MapPost("/contact/envoyer", async (HttpContext http, KontaktService kontakt) =>
        {
            var form = await http.Request.ReadFormAsync();
            if (!await AuthEndpunkte.AntiforgeryOkAsync(http)) return Results.LocalRedirect("/contact?erreur=session");
            // Honeypot: Roboter füllen das versteckte Feld – still « Erfolg » melden, nichts speichern.
            if (!string.IsNullOrEmpty(form["site"])) return Results.LocalRedirect("/contact?envoye=1");
            try
            {
                await kontakt.SendenAsync(new KontaktEingabe(form["nom"], form["email"], form["sujet"], form["message"]),
                    http.User.KontoId(), http.RequestAborted);
                return Results.LocalRedirect("/contact?envoye=1");
            }
            catch (FachFehler)
            {
                return Results.LocalRedirect("/contact?erreur=saisie");
            }
        }).RequireRateLimiting(RateLimits.Kontakt);

        app.MapPost("/api/postmark/inbound", async (HttpRequest request, IOptionsMonitor<PostmarkOptionen> optionen,
            PostEingangService posteingang, CancellationToken ct) =>
        {
            if (!PostEingangService.PruefeBasicAuth(request.Headers.Authorization.ToString(), optionen.CurrentValue.InboundWebhookSecret))
                return Results.Unauthorized();
            if (request.ContentLength > MaxWebhookBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            using var leser = new StreamReader(request.Body);
            var json = await leser.ReadToEndAsync(ct);
            var ergebnis = await posteingang.VerarbeiteAsync(json, ct);
            return ergebnis == EingangsErgebnis.Ungueltig ? Results.BadRequest() : Results.Ok();
        }).DisableAntiforgery().RequireRateLimiting(RateLimits.Webhook);
        return app;
    }
}
