using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Namen und Grenzen der Rate-Limit-Regeln. Gezählt wird je Internet-Anschluss (IP) – und eine Schule teilt sich oft
/// EINEN Anschluss (Schul-WLAN, Informatikraum, Mobilfunk-NAT). Die Grenzen müssen deshalb eine ganze Klasse tragen, die
/// sich gleichzeitig anmeldet, registriert oder beim ersten Login ihr Passwort wählt. Das Durchprobieren eines einzelnen
/// Kontos verhindert die Kontosperre (5 Fehlversuche / 15 Minuten), nicht diese Regel.
/// </summary>
public static class RateLimits
{
    public const string Anmeldung = "anmeldung";
    public const string Registrierung = "registrierung";
    /// <summary>Angemeldet das eigene Passwort ändern (auch Pflichtdialog beim ersten Login): je Konto, nicht je Anschluss.</summary>
    public const string Passwort = "passwort";
    /// <summary>Ohne Anmeldung: Link anfordern und neues Passwort setzen – je Anschluss.</summary>
    public const string PasswortOublie = "passwort-oublie";
    public const string Kontakt = "kontakt";
    public const string Webhook = "webhook";
    public const string Pruefung = "pruefung";

    public const int AnmeldungenJeMinute = 60;
    public const int RegistrierungenJeZehnMinuten = 120;
    public const int PasswortJeKontoJeZehnMinuten = 5;
    public const int PasswortOublieJeZehnMinuten = 20;

    /// <summary>Registriert die Regeln; abgelehnte Formulare kehren mit « erreur=limite » auf ihre Seite zurück.</summary>
    public static IServiceCollection AddSkoulouRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            Regel(o, Anmeldung, AnmeldungenJeMinute, TimeSpan.FromMinutes(1));
            Regel(o, Registrierung, RegistrierungenJeZehnMinuten, TimeSpan.FromMinutes(10));
            o.AddPolicy(Passwort, http => Fenster(PasswortSchluessel(http.User, http.Connection.RemoteIpAddress?.ToString()),
                PasswortJeKontoJeZehnMinuten, TimeSpan.FromMinutes(10)));
            Regel(o, PasswortOublie, PasswortOublieJeZehnMinuten, TimeSpan.FromMinutes(10));
            Regel(o, Kontakt, 5, TimeSpan.FromMinutes(10));
            Regel(o, Webhook, 120, TimeSpan.FromMinutes(1));
            Regel(o, Pruefung, 30, TimeSpan.FromMinutes(1));
            o.OnRejected = (kontext, _) =>
            {
                var http = kontext.HttpContext;
                var zurueck = http.Request.Headers.Referer.ToString();
                if (HttpMethods.IsPost(http.Request.Method) && Uri.TryCreate(zurueck, UriKind.Absolute, out var uri)
                    && uri.Host == http.Request.Host.Host && !http.Request.Path.StartsWithSegments("/api"))
                {
                    http.Response.StatusCode = StatusCodes.Status303SeeOther;
                    http.Response.Headers.Location = ZielNachAblehnung(uri);
                }
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    /// <summary>Angemeldet zählt das Konto (eine Klasse am selben Anschluss sperrt sich nicht gegenseitig aus), sonst der Anschluss.</summary>
    public static string PasswortSchluessel(System.Security.Claims.ClaimsPrincipal? nutzer, string? ip)
        => nutzer?.KontoId() is { } konto ? "konto:" + konto : "ip:" + (ip ?? "unbekannt");

    /// <summary>
    /// Rückweg nach einer Ablehnung: dieselbe Seite MIT ihren Parametern (z. B. id und token des Rücksetz-Links – sonst
    /// erschiene « lien invalide » statt « trop de tentatives ») und « erreur=limite »; alte Meldungs-Parameter fallen weg.
    /// </summary>
    public static string ZielNachAblehnung(Uri seite)
    {
        var parameter = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(seite.Query)
            .Where(p => p.Key is not ("erreur" or "erreur-mdp" or "info" or "envoye"))
            .SelectMany(p => p.Value.Select(wert => KeyValuePair.Create(p.Key, wert)))
            .Append(KeyValuePair.Create("erreur", (string?)"limite"));
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(seite.AbsolutePath, parameter);
    }

    private static void Regel(RateLimiterOptions o, string name, int anzahl, TimeSpan fenster)
        => o.AddPolicy(name, http => Fenster("ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt"), anzahl, fenster));

    private static RateLimitPartition<string> Fenster(string schluessel, int anzahl, TimeSpan fenster)
        => RateLimitPartition.GetFixedWindowLimiter(schluessel,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = anzahl, Window = fenster, QueueLimit = 0 });
}

/// <summary>Sicherheits-Header für alle Antworten (CSP ohne externe Quellen – keine CDNs).</summary>
public static class SicherheitsHeader
{
    public const string Csp =
        "default-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; font-src 'self' data:; frame-src 'self'; " +
        "frame-ancestors 'self'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static IApplicationBuilder UseSicherheitsHeader(this IApplicationBuilder app)
        => app.Use(async (http, weiter) =>
        {
            http.Response.OnStarting(() =>
            {
                var h = http.Response.Headers;
                h["X-Content-Type-Options"] = "nosniff";
                h["X-Frame-Options"] = "SAMEORIGIN";
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                if (!h.ContainsKey("Content-Security-Policy")) h["Content-Security-Policy"] = Csp;
                return Task.CompletedTask;
            });
            await weiter();
        });
}
