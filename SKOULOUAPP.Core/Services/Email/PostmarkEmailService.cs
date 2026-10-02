using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkoulouApp.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SkoulouApp.Core.Services.Email;

/// <summary>
/// E-Mail-Versand über die Postmark REST-API (https://api.postmarkapp.com).
/// Nutzt einen benannten HttpClient ("Postmark"), der in Program.cs konfiguriert wird.
/// </summary>
public sealed class PostmarkEmailService(
    IHttpClientFactory httpClientFactory,
    IOptions<PostmarkOptions> options,
    ILogger<PostmarkEmailService> logger) : IEmailService
{
    private const int BatchSize = 500; // Postmark-Limit pro Batch-Call.
    private readonly PostmarkOptions _options = options.Value;

    public async Task<EmailSendResult> SendAsync(EmailRecipient recipient, EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured(out var configError))
        {
            logger.LogWarning("Postmark ist nicht konfiguriert: {Error}. E-Mail an {Email} wird übersprungen.", configError, recipient.Email);
            return new EmailSendResult(false, configError);
        }

        var payload = BuildPayload(recipient, message);

        try
        {
            using var client = CreateClient();
            using var response = await client.PostAsJsonAsync("email", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new EmailSendResult(true);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Postmark-Versand an {Email} fehlgeschlagen ({Status}): {Body}", recipient.Email, (int)response.StatusCode, body);
            return new EmailSendResult(false, $"Erreur Postmark {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fehler beim Postmark-Versand an {Email}.", recipient.Email);
            return new EmailSendResult(false, ex.Message);
        }
    }

    public async Task<EmailBatchResult> SendBatchAsync(IReadOnlyCollection<EmailRecipient> recipients, EmailMessage message, CancellationToken cancellationToken = default)
    {
        var distinct = recipients
            .Where(r => !string.IsNullOrWhiteSpace(r.Email))
            .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (distinct.Count == 0)
        {
            return new EmailBatchResult(0, 0, []);
        }

        if (!IsConfigured(out var configError))
        {
            logger.LogWarning("Postmark ist nicht konfiguriert: {Error}. Massenversand übersprungen.", configError);
            return new EmailBatchResult(0, distinct.Count, [configError!]);
        }

        var sent = 0;
        var failed = 0;
        var errors = new List<string>();

        foreach (var batch in Chunk(distinct, BatchSize))
        {
            var payload = batch.Select(r => BuildPayload(r, message)).ToArray();

            try
            {
                using var client = CreateClient();
                using var response = await client.PostAsJsonAsync("email/batch", payload, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    failed += batch.Count;
                    errors.Add($"L'envoi groupé a échoué ({(int)response.StatusCode}).");
                    logger.LogError("Postmark-Batch fehlgeschlagen ({Status}): {Body}", (int)response.StatusCode, body);
                    continue;
                }

                // Postmark liefert ein Array mit Ergebnissen je Empfänger (ErrorCode 0 = OK).
                var (ok, notOk) = ParseBatchResult(body, batch.Count);
                sent += ok;
                failed += notOk;
            }
            catch (Exception ex)
            {
                failed += batch.Count;
                errors.Add(ex.Message);
                logger.LogError(ex, "Fehler beim Postmark-Batch-Versand.");
            }
        }

        return new EmailBatchResult(sent, failed, errors);
    }

    private static (int Ok, int NotOk) ParseBatchResult(string body, int fallbackCount)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (fallbackCount, 0);
            }

            var ok = 0;
            var notOk = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("ErrorCode", out var codeProp) && codeProp.TryGetInt32(out var code) && code != 0)
                {
                    notOk++;
                }
                else
                {
                    ok++;
                }
            }

            return (ok, notOk);
        }
        catch (JsonException)
        {
            return (fallbackCount, 0);
        }
    }

    private object BuildPayload(EmailRecipient recipient, EmailMessage message) => new
    {
        From = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromEmail
            : $"{_options.FromName} <{_options.FromEmail}>",
        To = string.IsNullOrWhiteSpace(recipient.Name)
            ? recipient.Email
            : $"{recipient.Name} <{recipient.Email}>",
        Subject = message.Subject,
        HtmlBody = message.HtmlBody,
        // Eine reine HTML-Mail ohne Textteil wird von Spamfiltern abgewertet.
        // Fehlt der Klartext, wird er aus dem HTML abgeleitet.
        TextBody = string.IsNullOrWhiteSpace(message.TextBody)
            ? FallbackText(message.HtmlBody)
            : message.TextBody,
        ReplyTo = message.ReplyTo,
        MessageStream = _options.MessageStream,
        // Open-/Link-Tracking schreibt alle Links auf eine Postmark-Trackingdomain um.
        // Das wird von Freemail-Filtern (web.de, GMX, Outlook) haeufig als Spam-Signal gewertet,
        // daher werden fuer transaktionale Mails die Originallinks beibehalten.
        TrackOpens = false,
        TrackLinks = "None"
    };

    /// <summary>
    /// Erzeugt einen einfachen Klartext aus HTML, damit jede Mail einen Textteil besitzt.
    /// </summary>
    private static string FallbackText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<br\\s*/?>|</p>|</div>|</tr>|</h[1-6]>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "[ \\t]+", " ");
        text = Regex.Replace(text, "(\\s*\\n\\s*){2,}", "\n\n");

        return text.Trim();
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("Postmark");
        client.DefaultRequestHeaders.Remove("X-Postmark-Server-Token");
        client.DefaultRequestHeaders.Add("X-Postmark-Server-Token", _options.ServerToken);
        return client;
    }

    private bool IsConfigured(out string? error)
    {
        if (!_options.Enabled)
        {
            error = "Postmark est désactivé (Postmark:Enabled=false).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.ServerToken))
        {
            error = "Postmark:ServerToken est manquant.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.FromEmail))
        {
            error = "Postmark:FromEmail est manquant.";
            return false;
        }

        error = null;
        return true;
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}
