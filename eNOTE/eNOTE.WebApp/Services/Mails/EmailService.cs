using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ENote.Core.DataModels.Infrastruktur;
using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services.Mails;

/// <summary>
/// Zentraler E-Mail-Versand der TabloSuite-Plattform.
/// <list type="bullet">
/// <item><c>Mail:Provider = Postmark</c> → Postmark-HTTP-API mit <c>Mail:PostmarkServerToken</c> (empfohlen).</item>
/// <item><c>Mail:Provider = Smtp</c> (oder nur <c>Mail:SmtpHost</c> gesetzt) → klassisches SMTP.</item>
/// <item>Nichts konfiguriert → Nachrichten landen nur in der Queue (<see cref="MailNachricht"/>).</item>
/// </list>
/// Jede Nachricht wird zuerst gespeichert und dann sofort versendet; Fehlversuche wiederholt
/// <see cref="MailVersandDienst"/> (max. <see cref="MaxVersuche"/>). Versandfehler blockieren nie den Aufrufer.
/// </summary>
public class EmailService(
    IConfiguration configuration,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<EmailService> logger,
    IHttpClientFactory? httpClientFactory = null)
{
    public const int MaxVersuche = 5;
    private const string PostmarkUrl = "https://api.postmarkapp.com/email";

    public string Absender => configuration["Mail:Absender"] is { Length: > 0 } a ? a : "contact@tablosuite.com";
    public string AbsenderName => configuration["Mail:AbsenderName"] is { Length: > 0 } n ? n : "TabloSuite";
    private string? AntwortAn => configuration["Mail:AntwortAn"] is { Length: > 0 } r ? r : null;
    private string? PostmarkToken => configuration["Mail:PostmarkServerToken"];
    private string? SmtpHost => configuration["Mail:SmtpHost"];
    private string MessageStream => configuration["Mail:MessageStream"] is { Length: > 0 } m ? m : "outbound";

    /// <summary>Gewählter Versandweg: „Postmark“, „Smtp“ oder „Aucun“.</summary>
    public string Anbieter
    {
        get
        {
            var gewaehlt = configuration["Mail:Provider"]?.Trim();
            if (string.Equals(gewaehlt, "Postmark", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(PostmarkToken) ? "Aucun" : "Postmark";
            if (string.Equals(gewaehlt, "Smtp", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(gewaehlt))
                return !string.IsNullOrWhiteSpace(SmtpHost) ? "Smtp" : !string.IsNullOrWhiteSpace(PostmarkToken) ? "Postmark" : "Aucun";
            return "Aucun";
        }
    }

    public bool IstKonfiguriert => Anbieter != "Aucun";

    /// <summary>Stellt eine E-Mail in die Queue und versucht den sofortigen Versand.</summary>
    public async Task SendeAsync(string empfaenger, string betreff, string htmlInhalt, CancellationToken cancellationToken = default)
        => await SendeAsync(empfaenger, betreff, htmlInhalt, kategorie: null, cancellationToken);

    /// <summary>Wie <see cref="SendeAsync(string,string,string,CancellationToken)"/>, mit Kategorie (Postmark-Tag).</summary>
    public async Task<bool> SendeAsync(string empfaenger, string betreff, string htmlInhalt, string? kategorie, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(empfaenger) || !MailAddress.TryCreate(empfaenger.Trim(), out _))
        {
            logger.LogWarning("E-Mail ohne gültigen Empfänger verworfen: {Betreff}", betreff);
            return false;
        }

        var nachricht = new MailNachricht
        {
            Empfaenger = empfaenger.Trim(),
            Betreff = betreff,
            HtmlInhalt = htmlInhalt,
            Kategorie = kategorie
        };

        var gespeichert = false;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            db.MailNachrichten.Add(nachricht);
            await db.SaveChangesAsync(cancellationToken);
            gespeichert = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mail-Queue nicht verfügbar; versuche Direktversand.");
        }

        if (!IstKonfiguriert)
        {
            logger.LogInformation("E-Mail-Versand nicht konfiguriert (Mail:Provider). Nachricht {Id} bleibt in der Queue.", nachricht.Id);
            return false;
        }

        var (erfolg, anbieterId, fehler) = await VersendeAsync(nachricht, cancellationToken);
        if (gespeichert)
            await MerkeErgebnisAsync(nachricht.Id, erfolg, anbieterId, fehler, cancellationToken);
        return erfolg;
    }

    /// <summary>Versendet noch offene Nachrichten erneut (vom Hintergrunddienst aufgerufen).</summary>
    public async Task<int> WiederholeAusstehendeAsync(CancellationToken ct = default)
    {
        if (!IstKonfiguriert) return 0;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var grenze = DateTime.UtcNow.AddDays(-3);
        var offene = await db.MailNachrichten
            .Where(m => !m.IstGesendet && m.Versuche < MaxVersuche && m.CreatedAtUtc >= grenze)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(25)
            .ToListAsync(ct);

        var gesendet = 0;
        foreach (var m in offene)
        {
            var (erfolg, anbieterId, fehler) = await VersendeAsync(m, ct);
            await MerkeErgebnisAsync(m.Id, erfolg, anbieterId, fehler, ct);
            if (erfolg) gesendet++;
        }

        return gesendet;
    }

    /// <summary>Letzte Nachrichten der Queue (für die Administration).</summary>
    public async Task<List<MailNachricht>> GetVerlaufAsync(int anzahl = 50, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.MailNachrichten.AsNoTracking().OrderByDescending(m => m.CreatedAtUtc).Take(anzahl).ToListAsync(ct);
    }

    /// <summary>Sendet eine (fehlgeschlagene) Nachricht sofort erneut.</summary>
    public async Task<bool> ErneutSendenAsync(Guid id, CancellationToken ct = default)
    {
        if (!IstKonfiguriert) return false;
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var m = await db.MailNachrichten.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return false;
        var (erfolg, anbieterId, fehler) = await VersendeAsync(m, ct);
        await MerkeErgebnisAsync(m.Id, erfolg, anbieterId, fehler, ct);
        return erfolg;
    }

    private async Task MerkeErgebnisAsync(Guid id, bool erfolg, string? anbieterId, string? fehler, CancellationToken ct)
    {
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var m = await db.MailNachrichten.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return;
            m.Versuche++;
            m.IstGesendet = erfolg;
            if (erfolg) m.GesendetUtc = DateTime.UtcNow;
            if (anbieterId is not null) m.AnbieterId = anbieterId;
            m.LetzterFehler = fehler is null ? null : fehler.Length > 1000 ? fehler[..1000] : fehler;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Versandstatus der Mail {Id} konnte nicht gespeichert werden.", id);
        }
    }

    private async Task<(bool Erfolg, string? AnbieterId, string? Fehler)> VersendeAsync(MailNachricht m, CancellationToken ct)
    {
        try
        {
            return Anbieter == "Postmark" ? await VersendePostmarkAsync(m, ct) : await VersendeSmtpAsync(m, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "E-Mail-Versand an {Empfaenger} fehlgeschlagen: {Betreff}", m.Empfaenger, m.Betreff);
            return (false, null, ex.Message);
        }
    }

    private async Task<(bool, string?, string?)> VersendePostmarkAsync(MailNachricht m, CancellationToken ct)
    {
        var client = httpClientFactory?.CreateClient("postmark") ?? new HttpClient();
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, PostmarkUrl)
        {
            Content = JsonContent.Create(new PostmarkNachricht(
                From: $"{AbsenderName} <{Absender}>",
                To: m.Empfaenger,
                Subject: m.Betreff,
                HtmlBody: m.HtmlInhalt,
                TextBody: AlsText(m.HtmlInhalt),
                ReplyTo: AntwortAn,
                Tag: m.Kategorie,
                MessageStream: MessageStream))
        };
        anfrage.Headers.Add("Accept", "application/json");
        anfrage.Headers.Add("X-Postmark-Server-Token", PostmarkToken);

        using var antwort = await client.SendAsync(anfrage, ct);
        var inhalt = await antwort.Content.ReadFromJsonAsync<PostmarkAntwort>(cancellationToken: ct);
        if (antwort.IsSuccessStatusCode && inhalt is { ErrorCode: 0 })
        {
            logger.LogInformation("E-Mail (Postmark) an {Empfaenger}: {Betreff}", m.Empfaenger, m.Betreff);
            return (true, inhalt.MessageID, null);
        }

        var fehler = $"Postmark {(int)antwort.StatusCode} / {inhalt?.ErrorCode}: {inhalt?.Message}";
        logger.LogError("E-Mail an {Empfaenger} abgelehnt: {Fehler}", m.Empfaenger, fehler);
        return (false, null, fehler);
    }

    private async Task<(bool, string?, string?)> VersendeSmtpAsync(MailNachricht m, CancellationToken ct)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(Absender, AbsenderName),
            Subject = m.Betreff,
            Body = m.HtmlInhalt,
            IsBodyHtml = true
        };
        message.To.Add(m.Empfaenger);
        if (AntwortAn is not null) message.ReplyToList.Add(AntwortAn);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(AlsText(m.HtmlInhalt), null, "text/plain"));

        using var smtp = new SmtpClient(SmtpHost, configuration.GetValue("Mail:SmtpPort", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(configuration["Mail:SmtpUtilisateur"], configuration["Mail:SmtpMotDePasse"])
        };
        await smtp.SendMailAsync(message, ct);
        logger.LogInformation("E-Mail (SMTP) an {Empfaenger}: {Betreff}", m.Empfaenger, m.Betreff);
        return (true, null, null);
    }

    /// <summary>Einfache Textfassung (Spamfilter bewerten reine HTML-Mails schlechter).</summary>
    public static string AlsText(string html)
    {
        var text = Regex.Replace(html, @"<(style|head)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<a [^>]*href=""([^""]+)""[^>]*>(.*?)</a>", "$2 ($1)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/p|/div|/h1|/h2|/tr|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");
        return text.Trim();
    }

    private sealed record PostmarkNachricht(
        string From, string To, string Subject, string HtmlBody, string TextBody,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag,
        string MessageStream);

    private sealed record PostmarkAntwort(int ErrorCode, string? Message, string? MessageID);
}
