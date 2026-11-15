using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Skoulou.Core.Entitaeten;
using Skoulou.Core.Rechte;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Formular-Endpunkte der Anmeldung (Cookies lassen sich nur per HTTP setzen): Anmelden, Abmelden,
/// Passwort ändern/vergessen/zurücksetzen – alle mit Antiforgery und Rate-Limit; dazu Alt-Routen.
/// </summary>
public static class AuthEndpunkte
{
    public static WebApplication MapAuthEndpunkte(this WebApplication app)
    {
        app.MapPost("/auth/connexion/envoyer", async (HttpContext http, AnmeldeService anmeldung, SignInManager<Konto> signIn) =>
        {
            var form = await http.Request.ReadFormAsync();
            var identifiant = form["identifiant"].ToString().Trim();
            var retour = LokalesZiel(form["retour"]);
            if (!await AntiforgeryOkAsync(http)) return Zurueck("/auth/connexion", "session", identifiant, retour);

            var pruefung = await anmeldung.PruefeAsync(identifiant, form["motdepasse"], http.RequestAborted);
            if (!AnmeldeRegeln.IstZugelassen(pruefung.Ergebnis) || pruefung.Konto is null)
                return Zurueck("/auth/connexion", AnmeldeRegeln.Code(pruefung.Ergebnis), identifiant, retour);

            await anmeldung.NeueSitzungAsync(pruefung.Konto, http.RequestAborted);
            await signIn.SignInAsync(pruefung.Konto, isPersistent: form["rester"] == "true");
            var ziel = pruefung.Ergebnis == AnmeldeErgebnis.Warteseite ? "/direction/en-attente"
                : retour is not null && retour != "/" ? retour : Rollen.Startseite(pruefung.Konto.Rolle);
            return Results.LocalRedirect(ziel);
        }).RequireRateLimiting(RateLimits.Anmeldung);

        app.MapPost("/auth/deconnexion", async (HttpContext http, AnmeldeService anmeldung, SignInManager<Konto> signIn) =>
        {
            if (!await AntiforgeryOkAsync(http)) return Results.LocalRedirect("/");
            if (http.User.KontoId() is { } kontoId) await anmeldung.SitzungBeendenAsync(kontoId, http.RequestAborted);
            await signIn.SignOutAsync();
            return Results.LocalRedirect("/auth/connexion?info=deconnecte");
        });

        app.MapPost("/auth/changer-mot-de-passe", async (HttpContext http, PasswortService passwoerter, SignInManager<Konto> signIn) =>
        {
            var form = await http.Request.ReadFormAsync();
            var retour = LokalesZiel(form["retour"]) ?? "/profil";
            if (!await AntiforgeryOkAsync(http)) return Results.LocalRedirect(MitParameter(retour, "erreur-mdp", "session"));
            if (http.User.KontoId() is not { } kontoId) return Results.LocalRedirect("/auth/connexion");

            var (fehler, konto) = await passwoerter.AendernAsync(kontoId, form["actuel"], form["nouveau"], form["confirmation"]);
            if (fehler is not null || konto is null) return Results.LocalRedirect(MitParameter(retour, "erreur-mdp", "1"));
            await signIn.RefreshSignInAsync(konto); // neues Cookie mit der neuen Sitzung, gleiche Dauer
            return Results.LocalRedirect(MitParameter(retour, "info", "mot-de-passe-modifie"));
        }).RequireAuthorization().RequireRateLimiting(RateLimits.Passwort);

        app.MapPost("/auth/mot-de-passe-oublie/envoyer", async (HttpContext http, PasswortService passwoerter) =>
        {
            var form = await http.Request.ReadFormAsync();
            if (!await AntiforgeryOkAsync(http)) return Results.LocalRedirect("/auth/mot-de-passe-oublie?erreur=session");
            await passwoerter.AnfordernAsync(form["saisie"], http.RequestAborted);
            return Results.LocalRedirect("/auth/mot-de-passe-oublie?envoye=1");
        }).RequireRateLimiting(RateLimits.PasswortOublie);

        app.MapPost("/auth/reinitialiser/envoyer", async (HttpContext http, PasswortService passwoerter) =>
        {
            var form = await http.Request.ReadFormAsync();
            var id = Guid.TryParse(form["id"], out var g) ? g : Guid.Empty;
            string token = form["token"].ToString();
            var zurueck = $"/auth/reinitialiser?id={id}&token={Uri.EscapeDataString(token)}";
            if (!await AntiforgeryOkAsync(http)) return Results.LocalRedirect(zurueck + "&erreur=session");

            var (fehler, identifiant) = await passwoerter.ZuruecksetzenAsync(id, token, form["nouveau"], form["confirmation"]);
            return fehler is null
                ? Results.LocalRedirect($"/auth/connexion?info=mot-de-passe-reinitialise&identifiant={Uri.EscapeDataString(identifiant ?? string.Empty)}")
                : Results.LocalRedirect(zurueck + "&erreur=" + (fehler == PasswortService.MeldungUngueltigerLink ? "lien" : "saisie"));
        }).RequireRateLimiting(RateLimits.PasswortOublie);

        // Alte Adressen (Links in Mails und QR-Codes) bleiben gültig.
        app.MapGet("/auth/login", (HttpContext h) => Results.LocalRedirect("/auth/connexion" + h.Request.QueryString));
        app.MapGet("/auth/register", (HttpContext h) => Results.LocalRedirect("/auth/inscription" + h.Request.QueryString));
        app.MapGet("/auth/forgot-password", () => Results.LocalRedirect("/auth/mot-de-passe-oublie"));
        app.MapGet("/auth/reset-password", (HttpContext h) => Results.LocalRedirect("/auth/reinitialiser" + h.Request.QueryString));
        app.MapGet("/verification-bulletin/{token}", (string token) =>
            Results.LocalRedirect("/verification/" + Uri.EscapeDataString(token)));
        return app;
    }

    /// <summary>Nur lokale Ziele (beginnt mit « / », nicht « // » oder « /\ »).</summary>
    public static string? LokalesZiel(string? url)
        => !string.IsNullOrWhiteSpace(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal)
           && !url.StartsWith("/\\", StringComparison.Ordinal) ? url : null;

    public static string MitParameter(string url, string name, string wert)
        => url + (url.Contains('?') ? "&" : "?") + name + "=" + Uri.EscapeDataString(wert);

    internal static async Task<bool> AntiforgeryOkAsync(HttpContext http)
    {
        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static IResult Zurueck(string seite, string code, string identifiant, string? retour)
    {
        var url = MitParameter(seite, "erreur", code);
        if (identifiant.Length is > 0 and <= 60) url = MitParameter(url, "identifiant", identifiant);
        if (retour is not null) url = MitParameter(url, "returnUrl", retour);
        return Results.LocalRedirect(url);
    }
}
