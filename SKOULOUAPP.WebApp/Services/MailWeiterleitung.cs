using System.Net.Http.Headers;
using System.Text;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// Gemeinsame Adresse contact@tablosuite.com für alle Apps: Postmark liefert eingehende Mails an SKOULOU.
/// Mails, die eNOTE betreffen (« eNOTE » im Betreff, nicht « [SKOULOU] »), werden unverändert an den
/// eNOTE-Webhook weitergeleitet (Basic-Auth « enote:&lt;Geheimnis&gt; »). Klappt das nicht, landen sie
/// zur Sicherheit im SKOULOU-Posteingang – es geht nichts verloren.
/// Konfiguration: Postmark:WeiterleitungEnoteUrl, Postmark:WeiterleitungEnoteGeheimnis.
/// </summary>
public sealed class MailWeiterleitung(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MailWeiterleitung> logger)
{
    public const string HttpClientName = "mail-weiterleitung";

    /// <summary>Gehört die Mail zu eNOTE? « [SKOULOU] » hat Vorrang (Antwort auf eine SKOULOU-Mail).</summary>
    public static bool GehoertZuEnote(string? betreff)
        => !string.IsNullOrWhiteSpace(betreff)
            && !betreff.Contains(SkoulouEmailService.AppKennung, StringComparison.OrdinalIgnoreCase)
            && betreff.Contains("enote", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Von den Apps selbst verschickte Mails (Kopie des Kontaktformulars, Hinweise an contact@) kommen über die
    /// gemeinsame Adresse zurück. Sie werden nicht erneut eingelesen (sonst Doppel und « Répondre » an sich selbst);
    /// im IONOS-Postfach bleiben sie trotzdem.
    /// </summary>
    public static bool IstEigeneAbsenderAdresse(string? von, params string?[] eigeneAdressen)
    {
        if (string.IsNullOrWhiteSpace(von) || !System.Net.Mail.MailAddress.TryCreate(von.Trim(), out var adresse))
            return false;
        return eigeneAdressen.Any(eigene => !string.IsNullOrWhiteSpace(eigene)
            && string.Equals(adresse.Address, eigene.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Leitet das Postmark-JSON an eNOTE weiter. true = eNOTE hat es angenommen.</summary>
    public async Task<bool> AnEnoteAsync(string postmarkJson, CancellationToken cancellationToken = default)
    {
        var url = configuration["Postmark:WeiterleitungEnoteUrl"];
        var geheimnis = configuration["Postmark:WeiterleitungEnoteGeheimnis"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(geheimnis))
        {
            logger.LogWarning("Mail für eNOTE erhalten, aber keine Weiterleitung konfiguriert – bleibt im SKOULOU-Posteingang.");
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(postmarkJson, Encoding.UTF8, "application/json")
            };
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("enote:" + geheimnis)));
            using var antwort = await client.SendAsync(anfrage, cancellationToken);
            if (antwort.IsSuccessStatusCode)
            {
                return true;
            }

            logger.LogWarning("Weiterleitung an eNOTE abgelehnt: {Status}", (int)antwort.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Weiterleitung an eNOTE fehlgeschlagen – Mail bleibt im SKOULOU-Posteingang.");
            return false;
        }
    }
}
