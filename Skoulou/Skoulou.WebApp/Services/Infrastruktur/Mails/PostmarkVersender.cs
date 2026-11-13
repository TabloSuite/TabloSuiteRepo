using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;

namespace Skoulou.WebApp.Services.Infrastruktur.Mails;

/// <summary>Ergebnis eines Versandversuchs.</summary>
public sealed record VersandErgebnis(bool Erfolg, string? PostmarkId, string? Fehler);

/// <summary>Technischer Versandweg (in Tests ersetzbar).</summary>
public interface IMailVersender
{
    /// <summary>Ist der Versand konfiguriert? Sonst bleiben Mails in der Outbox.</summary>
    bool IstBereit { get; }

    Task<VersandErgebnis> SendeAsync(MailAusgang mail, CancellationToken ct = default);
}

/// <summary>Versand über die Postmark-HTTP-API (ohne Open-/Link-Tracking wegen Spamfiltern).</summary>
public sealed class PostmarkVersender(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<PostmarkOptionen> optionen,
    ILogger<PostmarkVersender> logger) : IMailVersender
{
    public const string HttpClientName = "postmark";
    private const string Url = "https://api.postmarkapp.com/email";

    public bool IstBereit => optionen.CurrentValue.IstVersandBereit;

    public async Task<VersandErgebnis> SendeAsync(MailAusgang mail, CancellationToken ct = default)
    {
        var o = optionen.CurrentValue;
        if (!o.IstVersandBereit) return new VersandErgebnis(false, null, "Postmark n'est pas configuré.");
        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, Url)
            {
                Content = JsonContent.Create(new PostmarkNachricht(
                    $"{o.FromName} <{o.FromEmail}>", mail.An, mail.Betreff, mail.Html, mail.Text,
                    mail.AntwortAn, mail.Bezug, o.MessageStream, false, "None"))
            };
            anfrage.Headers.Add("Accept", "application/json");
            anfrage.Headers.Add("X-Postmark-Server-Token", o.ServerToken);
            using var antwort = await client.SendAsync(anfrage, ct);
            var inhalt = await antwort.Content.ReadFromJsonAsync<PostmarkAntwort>(cancellationToken: ct);
            if (antwort.IsSuccessStatusCode && inhalt is { ErrorCode: 0 })
                return new VersandErgebnis(true, inhalt.MessageID, null);
            return new VersandErgebnis(false, null, $"Postmark {(int)antwort.StatusCode} / {inhalt?.ErrorCode}: {inhalt?.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Postmark nicht erreichbar: {Meldung}", ex.Message);
            return new VersandErgebnis(false, null, ex.Message);
        }
    }

    private sealed record PostmarkNachricht(
        string From, string To, string Subject, string HtmlBody, string TextBody,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag,
        string MessageStream, bool TrackOpens, string TrackLinks);

    private sealed record PostmarkAntwort(int ErrorCode, string? Message, string? MessageID);
}
