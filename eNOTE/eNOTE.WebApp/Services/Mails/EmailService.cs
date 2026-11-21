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
/// Jede Nachricht wird nur gespeichert (Warteschlange) und der <see cref="MailVersandDienst"/> per <see cref="MailWeckruf"/>
/// geweckt: er versendet sie sofort im Hintergrund und wiederholt Fehlversuche (max. <see cref="MaxVersuche"/>).
/// Ein Klick wartet so nie auf Postmark/SMTP (Klicktest 20.11.: « J'ai payé » drehte 2–8 s, bei Ausfall bis 20 s).
/// Nur die Prüfmail der Administration (<see cref="SendeSofortAsync"/>) und « Renvoyer » senden direkt.
/// </summary>
public class EmailService(
    IConfiguration configuration,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<EmailService> logger,
    IHttpClientFactory? httpClientFactory = null,
    MailWeckruf? weckruf = null)
{
    public const int MaxVersuche = 5;

    /// <summary>So viele neue Mails versendet der Dienst je Durchgang.</summary>
    public const int StapelGroesse = 25;

    /// <summary>Kategorie der Passwort-vergessen-Mail: kommt in der Warteschlange vor allen anderen dran.</summary>
    public const string KategorieVorrang = "reinitialisation";

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

    /// <summary>Monatliches Kontingent des Postmark-Plans (nur Anzeige/Warnung, Standard 100).</summary>
    public int MonatsLimit => configuration.GetValue("Mail:MonatsLimit", 100);

    /// <summary>
    /// Darf eine Mail dieser Kategorie versendet werden? Pflicht- und manuelle Mails immer; abschaltbare
    /// nach Schalter (ohne Eintrag: Standard der Mail-Art); unbekannte Kategorien sind erlaubt.
    /// </summary>
    public async Task<bool> IstAktivAsync(string? kategorie, CancellationToken ct = default)
    {
        if (kategorie is null || MailArten.Manuell.Contains(kategorie)) return true;
        var art = MailArten.Finde(kategorie);
        if (art is null || art.Pflicht) return true;
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var regel = await db.MailRegeln.AsNoTracking().FirstOrDefaultAsync(r => r.Kategorie == kategorie, ct);
            return regel?.Aktiv ?? art.StandardAktiv;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mail-Schalter für {Kategorie} nicht lesbar – Standard wird verwendet.", kategorie);
            return art.StandardAktiv;
        }
    }

    /// <summary>Aktueller Zustand aller abschaltbaren Mail-Arten.</summary>
    public async Task<Dictionary<string, bool>> GetSchalterAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var regeln = await db.MailRegeln.AsNoTracking().ToDictionaryAsync(r => r.Kategorie, r => r.Aktiv, ct);
        return MailArten.Alle.Where(a => !a.Pflicht)
            .ToDictionary(a => a.Kategorie, a => regeln.TryGetValue(a.Kategorie, out var an) ? an : a.StandardAktiv);
    }

    /// <summary>Schaltet eine abschaltbare Mail-Art ein oder aus (nur SuperAdmin).</summary>
    public async Task SetzeSchalterAsync(Guid bearbeiter, string kategorie, bool aktiv, CancellationToken ct = default)
    {
        var art = MailArten.Finde(kategorie) ?? throw new ArgumentException("Type d'e-mail inconnu.");
        if (art.Pflicht) throw new InvalidOperationException("Cet e-mail est indispensable et ne peut pas être désactivé.");
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await AdminKontenService.PruefeAsync(db, bearbeiter, false, ct);
        var regel = await db.MailRegeln.FirstOrDefaultAsync(r => r.Kategorie == kategorie, ct);
        if (regel is null) db.MailRegeln.Add(regel = new MailRegel { Kategorie = kategorie });
        regel.Aktiv = aktiv;
        regel.GeaendertUtc = DateTime.UtcNow;
        regel.GeaendertVonId = bearbeiter;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Im laufenden Kalendermonat erfolgreich versendete Mails (für das Kontingent).</summary>
    public async Task<int> GesendetImMonatAsync(CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var jetzt = DateTime.UtcNow;
        var anfang = new DateTime(jetzt.Year, jetzt.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await db.MailNachrichten.CountAsync(m => m.IstGesendet && m.GesendetUtc >= anfang, ct);
    }

    /// <summary>App-Kennung im Betreff: alle Apps teilen contact@tablosuite.com, Antworten werden daran zugeordnet.</summary>
    public const string AppKennung = "[eNOTE]";

    /// <summary>Setzt « [eNOTE] » vor den Betreff (nicht doppelt, auch nicht bei « Re: [eNOTE] … »).</summary>
    public static string MitAppName(string betreff)
        => betreff.Contains(AppKennung, StringComparison.OrdinalIgnoreCase) ? betreff : $"{AppKennung} {betreff}".TrimEnd();

    /// <summary>Stellt eine E-Mail in die Warteschlange; der Hintergrunddienst versendet sie gleich danach.</summary>
    public async Task SendeAsync(string empfaenger, string betreff, string htmlInhalt, CancellationToken cancellationToken = default)
        => await SendeAsync(empfaenger, betreff, htmlInhalt, kategorie: null, cancellationToken);

    /// <summary>
    /// Wie <see cref="SendeAsync(string,string,string,CancellationToken)"/>, mit Kategorie (Postmark-Tag). Wartet nicht
    /// auf den Versand. true = eingereiht und ein Versandweg ist konfiguriert (die Mail geht « dans un instant » raus);
    /// false = verworfen (ungültige Adresse, abgeschaltete Mail-Art) oder kein Versandweg (bleibt in der Warteschlange).
    /// </summary>
    public Task<bool> SendeAsync(string empfaenger, string betreff, string htmlInhalt, string? kategorie, CancellationToken cancellationToken = default)
        => SpeichereUndSendeAsync(empfaenger, betreff, htmlInhalt, kategorie, sofort: false, cancellationToken);

    /// <summary>
    /// Prüfmail der Administration: speichert und versendet direkt, damit der Administrator das Ergebnis des
    /// Versandwegs sofort sieht. Für alle anderen Mails <see cref="SendeAsync(string,string,string,string?,CancellationToken)"/>.
    /// </summary>
    public Task<bool> SendeSofortAsync(string empfaenger, string betreff, string htmlInhalt, string? kategorie, CancellationToken cancellationToken = default)
        => SpeichereUndSendeAsync(empfaenger, betreff, htmlInhalt, kategorie, sofort: true, cancellationToken);

    private async Task<bool> SpeichereUndSendeAsync(string empfaenger, string betreff, string htmlInhalt, string? kategorie, bool sofort,
        CancellationToken cancellationToken)
    {
        betreff = MitAppName(betreff);
        if (string.IsNullOrWhiteSpace(empfaenger) || !MailAddress.TryCreate(empfaenger.Trim(), out _))
        {
            logger.LogWarning("E-Mail ohne gültigen Empfänger verworfen: {Betreff}", betreff);
            return false;
        }

        if (!await IstAktivAsync(kategorie, cancellationToken))
        {
            logger.LogInformation("E-Mail « {Kategorie} » ist abgeschaltet – nicht versendet: {Betreff}", kategorie, betreff);
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

        if (gespeichert && !sofort)
        {
            // Versand übernimmt der Hintergrunddienst – der Klick wartet nicht auf Postmark/SMTP.
            weckruf?.Wecken();
            return true;
        }

        // Prüfmail – oder Warteschlange nicht erreichbar: direkt versenden (Ausnahmefall).
        var (erfolg, anbieterId, fehler) = await VersendeAsync(nachricht, cancellationToken);
        if (gespeichert)
            await MerkeErgebnisAsync(nachricht.Id, erfolg, anbieterId, fehler, cancellationToken);
        return erfolg;
    }

    /// <summary>
    /// Versendet neu eingereihte Mails (noch kein Versuch), « Mot de passe oublié » zuerst, dann in Eingangsreihenfolge
    /// (vom Hintergrunddienst nach jedem Weckruf aufgerufen). <c>Mehr</c> = es warten weitere neue Mails.
    /// </summary>
    public async Task<(int Gesendet, bool Mehr)> VersendeNeueAsync(CancellationToken ct = default)
    {
        if (!IstKonfiguriert) return (0, false);
        List<MailNachricht> neue;
        await using (var db = await dbContextFactory.CreateDbContextAsync(ct))
        {
            var grenze = DateTime.UtcNow.AddDays(-3);
            neue = await db.MailNachrichten.AsNoTracking()
                .Where(m => !m.IstGesendet && m.Versuche == 0 && m.CreatedAtUtc >= grenze)
                .OrderBy(m => m.Kategorie == KategorieVorrang ? 0 : 1)
                .ThenBy(m => m.CreatedAtUtc)
                .Take(StapelGroesse + 1)
                .ToListAsync(ct);
        }

        var mehr = neue.Count > StapelGroesse;
        var gesendet = 0;
        foreach (var m in neue.Take(StapelGroesse))
        {
            var (erfolg, anbieterId, fehler) = await VersendeAsync(m, ct);
            await MerkeErgebnisAsync(m.Id, erfolg, anbieterId, fehler, ct);
            if (erfolg) gesendet++;
        }
        return (gesendet, mehr);
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

    /// <summary>
    /// Verlauf seitenweise und durchsuchbar (Betreff oder Empfänger) – auch nach Jahren wird nie alles geladen.
    /// Der Inhalt (HTML) wird nicht mitgeladen, sondern erst beim Öffnen (<see cref="GetInhaltAsync"/>).
    /// </summary>
    public async Task<(List<MailNachricht> Eintraege, bool Mehr)> SucheVerlaufAsync(
        string? suche, int ueberspringen, int anzahl = 50, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var abfrage = db.MailNachrichten.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(suche))
        {
            var s = suche.Trim();
            abfrage = abfrage.Where(m => m.Betreff.Contains(s) || m.Empfaenger.Contains(s));
        }

        var liste = await abfrage
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip(Math.Max(0, ueberspringen))
            .Take(anzahl + 1)
            .Select(m => new MailNachricht
            {
                Id = m.Id, Empfaenger = m.Empfaenger, Betreff = m.Betreff, IstGesendet = m.IstGesendet,
                CreatedAtUtc = m.CreatedAtUtc, GesendetUtc = m.GesendetUtc, Kategorie = m.Kategorie,
                Versuche = m.Versuche, LetzterFehler = m.LetzterFehler
            })
            .ToListAsync(ct);
        var mehr = liste.Count > anzahl;
        if (mehr) liste.RemoveAt(liste.Count - 1);
        return (liste, mehr);
    }

    /// <summary>HTML-Inhalt einer versendeten Mail (für die Ansicht in der Administration).</summary>
    public async Task<string?> GetInhaltAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.MailNachrichten.AsNoTracking().Where(m => m.Id == id).Select(m => m.HtmlInhalt).FirstOrDefaultAsync(ct);
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
        var text = Regex.Replace(html, @"<(style|head|title)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        // Versteckte Vorschauzeile und Logo-Kachel gehören nicht in die Textfassung.
        text = Regex.Replace(text, @"<div style=""display:none[^""]*"">.*?</div>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<td style=""width:36px[^""]*"">T</td>", "", RegexOptions.IgnoreCase);
        // Tabellen: Label und Wert trennen (jede Tabellenzeile wird eine Textzeile).
        text = Regex.Replace(text, @"</td>\s*<td[^>]*>", " : ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<a [^>]*href=""([^""]+)""[^>]*>(.*?)</a>", "$2 ($1)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/p|/div|/h1|/h2|/tr|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private sealed record PostmarkNachricht(
        string From, string To, string Subject, string HtmlBody, string TextBody,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReplyTo,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag,
        string MessageStream);

    private sealed record PostmarkAntwort(int ErrorCode, string? Message, string? MessageID);
}
