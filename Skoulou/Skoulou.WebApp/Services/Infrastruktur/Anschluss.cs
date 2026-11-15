using System.Net;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Internet-Anschluss (Adresse) des Besuchers – Schlüssel der Schutzgrenzen (<see cref="RateLimits"/>, Registrierungsbremse).
/// Auf Azure App Service steht vor der App ein Eingangsserver: ohne ausgewertete Weiterleitungs-Kopfzeilen sähe die App
/// für ALLE Besucher dessen Adresse, und alle Schulen teilten sich einen Zähler. Hier wird deshalb unabhängig von der
/// Einstellung der Plattform die richtige Adresse bestimmt, ohne die Anfrage selbst zu verändern.
/// </summary>
public static class Anschluss
{
    private static readonly bool HinterAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

    public static string Adresse(HttpContext http)
        => Adresse(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["X-Forwarded-For"].ToString(),
            http.Request.Headers.ContainsKey("X-Original-For"), HinterAzure);

    /// <summary>
    /// <paramref name="schonAusgewertet"/>: das Framework hat die Weiterleitung bereits ausgewertet (es setzt dann
    /// « X-Original-For ») – die Verbindungsadresse ist schon die des Besuchers, übrige Einträge sind nicht vertrauenswürdig.
    /// Sonst gilt hinter Azure der LETZTE Eintrag von « X-Forwarded-For »: ihn hängt der Eingangsserver selbst an
    /// (« 203.0.113.5:51234 »); Einträge davor kann der Besucher frei erfinden. Außerhalb von Azure zählt nur die Verbindung.
    /// </summary>
    public static string Adresse(string? verbindung, string? weitergeleitet, bool schonAusgewertet, bool hinterAzure)
    {
        if (hinterAzure && !schonAusgewertet && !string.IsNullOrWhiteSpace(weitergeleitet)
            && IPEndPoint.TryParse(weitergeleitet.Split(',')[^1].Trim(), out var besucher))
            return besucher.Address.ToString();
        return string.IsNullOrWhiteSpace(verbindung) ? "unbekannt" : verbindung;
    }
}
