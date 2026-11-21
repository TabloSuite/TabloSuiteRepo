using System.Globalization;
using Microsoft.AspNetCore.Http;
using Skoulou.Core.Fehler;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// POST /auth/inscription/envoyer: Selbstregistrierung als klassisches Formular (Antiforgery, Honeypot,
/// Rate-Limit je IP). Die Seite prüft vorher in Blazor (Meldungen am Feld) und öffnet die Nutzungsbedingungen.
/// </summary>
public static class InscriptionEndpunkt
{
    public static WebApplication MapInscriptionEndpunkt(this WebApplication app)
    {
        app.MapPost("/auth/inscription/envoyer", async (HttpContext http, RegistrierungService registrierung) =>
        {
            var form = await http.Request.ReadFormAsync();
            var art = form["type"] == "eleve" ? RegistrierungsArt.Eleve : RegistrierungsArt.Direction;
            var zurueck = "/auth/inscription?type=" + (art == RegistrierungsArt.Eleve ? "eleve" : "direction");
            if (!await AuthEndpunkte.AntiforgeryOkAsync(http)) return Results.LocalRedirect(zurueck + "&erreur=session");
            // Honeypot: Roboter füllen das versteckte Feld – still « Erfolg » melden, nichts anlegen.
            if (!string.IsNullOrEmpty(form["site"])) return Results.LocalRedirect("/auth/inscription?envoye=" + (art == RegistrierungsArt.Eleve ? "eleve" : "direction"));
            try
            {
                var ergebnis = await registrierung.RegistrierenAsync(Lies(form, art), Anschluss.Adresse(http), http.RequestAborted);
                return Results.LocalRedirect($"/auth/inscription?envoye={(art == RegistrierungsArt.Eleve ? "eleve" : "direction")}&identifiant={Uri.EscapeDataString(ergebnis.Identifiant)}");
            }
            catch (FachFehler f)
            {
                // Konkreter Code statt nur « saisie »; die Eingaben (ohne Passwörter) setzt die Seite aus dem Browser wieder ein.
                return Results.LocalRedirect(zurueck + "&erreur=" + InscriptionFehler.Code(f));
            }
        }).RequireRateLimiting(RateLimits.Registrierung);
        return app;
    }

    private static RegistrierungsEingabe Lies(IFormCollection f, RegistrierungsArt art)
    {
        static string? W(IFormCollection f, string name) => string.IsNullOrWhiteSpace(f[name]) ? null : f[name].ToString();
        return new RegistrierungsEingabe(art, W(f, "prenom"), W(f, "nom"), W(f, "identifiant"), W(f, "email"), W(f, "telephone"),
            f["motdepasse"].ToString(), f["confirmation"].ToString(), f["agb"] == "oui",
            W(f, "ecole-nom"), W(f, "ville"), W(f, "pays"),
            Guid.TryParse(f["ecole"], out var schule) ? schule : null,
            DateOnly.TryParseExact(f["naissance"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var g) ? g : null,
            W(f, "parent-tel"), W(f, "parent-email"));
    }
}
