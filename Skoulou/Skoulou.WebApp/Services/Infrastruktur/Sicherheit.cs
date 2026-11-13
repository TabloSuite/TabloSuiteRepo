using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Namen der Rate-Limit-Regeln (je IP).</summary>
public static class RateLimits
{
    public const string Anmeldung = "anmeldung";
    public const string Registrierung = "registrierung";
    public const string Passwort = "passwort";
    public const string Kontakt = "kontakt";
    public const string Webhook = "webhook";
    public const string Pruefung = "pruefung";

    /// <summary>Registriert die Regeln; abgelehnte Formulare kehren mit « erreur=limite » zurück.</summary>
    public static IServiceCollection AddSkoulouRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            Regel(o, Anmeldung, 10, TimeSpan.FromMinutes(1));
            Regel(o, Registrierung, 5, TimeSpan.FromMinutes(10));
            Regel(o, Passwort, 5, TimeSpan.FromMinutes(10));
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
                    http.Response.Headers.Location = uri.AbsolutePath + "?erreur=limite";
                }
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }

    private static void Regel(RateLimiterOptions o, string name, int anzahl, TimeSpan fenster)
        => o.AddPolicy(name, http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = anzahl, Window = fenster, QueueLimit = 0 }));
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
