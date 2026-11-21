using System.Collections.Concurrent;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ENote.WebApp.Services.Sicherheit;

/// <summary>
/// Internet-Anschluss (Adresse) des Besuchers – Schlüssel der Schutzgrenzen. Auf Azure App Service steht vor der App ein
/// Eingangsserver: ohne ausgewertete Weiterleitungs-Kopfzeilen sähe die App für ALLE Besucher dessen Adresse, und alle
/// Läden teilten sich einen Zähler. Hier wird die Adresse unabhängig von der Einstellung der Plattform bestimmt, ohne
/// die Anfrage zu verändern (gleiche Regel wie in SKOULOU).
/// </summary>
public static class Anschluss
{
    private static readonly bool HinterAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

    /// <summary>Adresse des Besuchers oder null (keine Anfrage bekannt – dann greift die Grenze je Anschluss nicht).</summary>
    public static string? Adresse(HttpContext? http)
        => http is null ? null
            : Adresse(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["X-Forwarded-For"].ToString(),
                http.Request.Headers.ContainsKey("X-Original-For"), HinterAzure);

    /// <summary>
    /// <paramref name="schonAusgewertet"/>: das Framework hat die Weiterleitung bereits ausgewertet (es setzt dann
    /// « X-Original-For ») – die Verbindungsadresse ist schon die des Besuchers. Sonst gilt hinter Azure der LETZTE Eintrag
    /// von « X-Forwarded-For »: ihn hängt der Eingangsserver selbst an (« 203.0.113.5:51234 »); Einträge davor kann der
    /// Besucher frei erfinden. Außerhalb von Azure zählt nur die Verbindung.
    /// </summary>
    public static string? Adresse(string? verbindung, string? weitergeleitet, bool schonAusgewertet, bool hinterAzure)
    {
        if (hinterAzure && !schonAusgewertet && !string.IsNullOrWhiteSpace(weitergeleitet)
            && IPEndPoint.TryParse(weitergeleitet.Split(',')[^1].Trim(), out var besucher))
            return besucher.Address.ToString();
        return string.IsNullOrWhiteSpace(verbindung) ? null : verbindung;
    }
}

/// <summary>Höchstens <paramref name="Max"/> Vorgänge im Zeitfenster.</summary>
public readonly record struct Grenze(int Max, TimeSpan Fenster);

/// <summary>
/// Bremse gegen Massenmissbrauch der öffentlichen Formulare (zusätzlich zur Kontosperre nach Fehlversuchen): zählt
/// Vorgänge je Anschluss, je E-Mail-Adresse und je Konto im Arbeitsspeicher. Das Kontaktformular hat seine eigene Bremse
/// (<see cref="KontaktService"/>). Die Grenzen sind bewusst großzügig: ein Laden teilt sich einen Anschluss.
/// </summary>
public sealed class MissbrauchsBremse(TimeProvider? zeit = null)
{
    public const string MeldungInscription =
        "Trop d'inscriptions depuis cette connexion ou avec cette adresse e-mail. Réessayez demain ou écrivez à contact@tablosuite.com.";

    // 21.11.2026: 10 → 50 je Anschluss (Mobilfunk in Togo/Kamerun: viele Kunden hinter einer Adresse; Vorführung mit einem WLAN).
    // Die Grenze je E-Mail-Adresse (3) bremst weiter Missbrauch.
    public static readonly Grenze InscriptionJeAnschluss = new(50, TimeSpan.FromHours(24));
    public static readonly Grenze InscriptionJeAdresse = new(3, TimeSpan.FromHours(24));
    public static readonly Grenze OublieJeKonto = new(3, TimeSpan.FromHours(1));
    public static readonly Grenze OublieJeAnschluss = new(10, TimeSpan.FromMinutes(10));

    private const int MerkGrenze = 500;
    private readonly TimeProvider uhr = zeit ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> zaehler = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ist ein weiterer Vorgang erlaubt? Ohne Schlüssel (Anschluss unbekannt) greift diese Grenze nicht.</summary>
    public bool IstErlaubt(string bereich, string? schluessel, Grenze grenze)
    {
        if (string.IsNullOrWhiteSpace(schluessel)) return true;
        if (!zaehler.TryGetValue(bereich + ":" + schluessel.Trim(), out var liste)) return true;
        var ab = uhr.GetUtcNow() - grenze.Fenster;
        lock (liste)
        {
            liste.RemoveAll(t => t < ab);
            return liste.Count < grenze.Max;
        }
    }

    /// <summary>Merkt einen Vorgang.</summary>
    public void Merken(string bereich, string? schluessel)
    {
        if (string.IsNullOrWhiteSpace(schluessel)) return;
        var liste = zaehler.GetOrAdd(bereich + ":" + schluessel.Trim(), _ => []);
        lock (liste)
        {
            liste.Add(uhr.GetUtcNow());
            if (liste.Count > MerkGrenze) liste.RemoveRange(0, liste.Count - MerkGrenze);
        }
    }
}

/// <summary>Rate-Limit der Anmeldung (klassisches Formular): je Anschluss, großzügig für ein ganzes Team am selben WLAN.</summary>
public static class Schutzgrenzen
{
    public const string Anmeldung = "anmeldung";
    public const int AnmeldungenJeMinute = 60;
    public const string MeldungLimite = "Trop de tentatives en peu de temps. Patientez quelques minutes, puis réessayez.";

    public static IServiceCollection AddEnoteSchutzgrenzen(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Anmeldung, http => RateLimitPartition.GetFixedWindowLimiter("ip:" + (Anschluss.Adresse(http) ?? "unbekannt"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = AnmeldungenJeMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = (kontext, _) =>
            {
                var http = kontext.HttpContext;
                if (HttpMethods.IsPost(http.Request.Method) && http.Request.Path.StartsWithSegments("/auth/login"))
                {
                    http.Response.StatusCode = StatusCodes.Status303SeeOther;
                    http.Response.Headers.Location = "/auth/login?error=limite";
                }
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }
}
