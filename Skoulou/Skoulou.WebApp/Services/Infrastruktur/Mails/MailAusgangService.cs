using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur.Mails;

/// <summary>Weckt den Versanddienst, sobald eine Mail eingereiht wurde.</summary>
public sealed class MailSignal
{
    private readonly SemaphoreSlim signal = new(0, 1);

    public void Wecken()
    {
        if (signal.CurrentCount == 0)
        {
            try { signal.Release(); } catch (SemaphoreFullException) { }
        }
    }

    /// <summary>Wartet auf ein Signal oder bis die Zeit abläuft.</summary>
    public Task WarteAsync(TimeSpan hoechstens, CancellationToken ct) => signal.WaitAsync(hoechstens, ct);
}

/// <summary>
/// Outbox: Mails werden mit Layout und Präfix « [SKOULOU] » gespeichert und im Hintergrund versendet;
/// Fehlversuche werden mit wachsendem Abstand wiederholt. Versandfehler blockieren nie den Aufrufer.
/// </summary>
public sealed class MailAusgangService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    IMailVersender versender,
    MailSignal signal,
    TimeProvider zeit,
    ILogger<MailAusgangService> logger)
{
    public const string AppKennung = "[SKOULOU]";
    public const int MaxVersuche = 6;

    /// <summary>« [SKOULOU] » voranstellen, aber nie doppelt (auch nicht bei « Re: [SKOULOU] … »).</summary>
    public static string MitAppKennung(string betreff)
    {
        var b = (betreff ?? string.Empty).Trim();
        return b.Contains(AppKennung, StringComparison.OrdinalIgnoreCase) ? b : $"{AppKennung} {b}".TrimEnd();
    }

    /// <summary>Plausible Adresse?</summary>
    public static bool IstAdresse(string? adresse)
        => !string.IsNullOrWhiteSpace(adresse) && MailAddress.TryCreate(adresse.Trim(), out var a)
           && a.Address.Equals(adresse.Trim(), StringComparison.OrdinalIgnoreCase) && a.Host.Contains('.');

    /// <summary>Wartezeit vor dem nächsten Versuch (1, 5, 15, 60, 240 Minuten).</summary>
    public static TimeSpan Wartezeit(int versuche) => versuche switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromMinutes(60),
        _ => TimeSpan.FromMinutes(240)
    };

    /// <summary>Reiht eine Mail ein (Layout wird ergänzt). null, wenn die Adresse unbrauchbar ist.</summary>
    public async Task<Guid?> EinreihenAsync(string an, string titel, string inhaltHtml, string? bezug = null,
        string? antwortAn = null, string? vorschau = null, CancellationToken ct = default)
    {
        if (!IstAdresse(an))
        {
            logger.LogWarning("Mail ohne gültige Adresse verworfen ({Bezug}).", bezug);
            return null;
        }

        var html = MailLayout.Umhuelle(titel, inhaltHtml, vorschau);
        var mail = new MailAusgang
        {
            An = an.Trim(),
            Betreff = Kuerze(MitAppKennung(titel), 300),
            Html = html,
            Text = MailLayout.AlsText(html),
            AntwortAn = IstAdresse(antwortAn) ? antwortAn!.Trim() : null,
            Bezug = bezug,
            Status = MailStatus.Offen,
            NaechsterVersuchUtc = zeit.GetUtcNow().UtcDateTime
        };
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.MailAusgaenge.Add(mail);
        await db.SaveChangesAsync(ct);
        signal.Wecken();
        return mail.Id;
    }

    /// <summary>Versendet fällige Mails (vom Hintergrunddienst aufgerufen). Liefert die Zahl der gesendeten.</summary>
    public async Task<int> VerarbeiteFaelligeAsync(CancellationToken ct = default)
    {
        if (!versender.IstBereit) return 0;
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var faellige = await db.MailAusgaenge
            .Where(m => m.Status == MailStatus.Offen && m.NaechsterVersuchUtc <= jetzt)
            .OrderBy(m => m.NaechsterVersuchUtc)
            .Take(20)
            .ToListAsync(ct);

        var gesendet = 0;
        foreach (var mail in faellige)
        {
            var ergebnis = await versender.SendeAsync(mail, ct);
            mail.Versuche++;
            if (ergebnis.Erfolg)
            {
                mail.Status = MailStatus.Gesendet;
                mail.GesendetAmUtc = zeit.GetUtcNow().UtcDateTime;
                mail.PostmarkId = ergebnis.PostmarkId;
                mail.Fehler = null;
                gesendet++;
            }
            else
            {
                mail.Fehler = Kuerze(ergebnis.Fehler ?? "Erreur inconnue", 1000);
                if (mail.Versuche >= MaxVersuche) mail.Status = MailStatus.Fehlgeschlagen;
                else mail.NaechsterVersuchUtc = zeit.GetUtcNow().UtcDateTime + Wartezeit(mail.Versuche);
                logger.LogWarning("Mail {Id} nicht versendet (Versuch {Versuch}): {Fehler}", mail.Id, mail.Versuche, mail.Fehler);
            }
            await db.SaveChangesAsync(ct);
        }
        return gesendet;
    }

    private static string Kuerze(string text, int max) => text.Length > max ? text[..max] : text;
}

/// <summary>Hintergrunddienst: versendet die Outbox (sofort nach Signal, sonst alle 30 s).</summary>
public sealed class MailVersandDienst(IServiceScopeFactory scopes, MailSignal signal, ILogger<MailVersandDienst> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dienst = scope.ServiceProvider.GetRequiredService<MailAusgangService>();
                await dienst.VerarbeiteFaelligeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Mailversand fehlgeschlagen.");
            }

            try { await signal.WarteAsync(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
