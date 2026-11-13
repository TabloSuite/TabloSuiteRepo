using Microsoft.EntityFrameworkCore;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Täglicher Hausputz (idempotent): Fehlerjournal nach 90 Tagen, versendete Mails und Nachrichten
/// nach 2 Jahren. Audit bleibt unbegrenzt.
/// </summary>
public sealed class AufraeumDienst(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    TimeProvider zeit,
    ILogger<AufraeumDienst> logger) : BackgroundService
{
    public static readonly TimeSpan FehlerAufbewahrung = TimeSpan.FromDays(90);
    public static readonly TimeSpan MailAufbewahrung = TimeSpan.FromDays(730);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var (fehler, mails, nachrichten) = await BereinigeAsync(stoppingToken);
                if (fehler + mails + nachrichten > 0)
                    logger.LogInformation("Aufräumen: {Fehler} Fehler, {Mails} Mails, {Nachrichten} Nachrichten entfernt.", fehler, mails, nachrichten);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Aufräumdienst fehlgeschlagen.");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Entfernt abgelaufene Einträge; liefert die Anzahlen.</summary>
    public async Task<(int Fehler, int Mails, int Nachrichten)> BereinigeAsync(CancellationToken ct = default)
    {
        var jetzt = zeit.GetUtcNow().UtcDateTime;
        var fehlerGrenze = jetzt - FehlerAufbewahrung;
        var mailGrenze = jetzt - MailAufbewahrung;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (db.Database.IsSqlServer())
        {
            var f = await db.FehlerProtokolle.Where(x => x.ZeitUtc < fehlerGrenze).ExecuteDeleteAsync(ct);
            var m = await db.MailAusgaenge.Where(x => x.ErstelltAmUtc < mailGrenze && x.Status != MailStatus.Offen).ExecuteDeleteAsync(ct);
            // Nachrichten mit Antworten bleiben, bis die Antworten selbst ablaufen (FK Restrict).
            var n = await db.Nachrichten.Where(x => x.ErstelltAmUtc < mailGrenze && !db.Nachrichten.Any(a => a.AntwortAufId == x.Id))
                .ExecuteDeleteAsync(ct);
            return (f, m, n);
        }

        // InMemory (Tests): ExecuteDelete wird nicht unterstützt.
        var alteFehler = await db.FehlerProtokolle.Where(x => x.ZeitUtc < fehlerGrenze).ToListAsync(ct);
        var alteMails = await db.MailAusgaenge.Where(x => x.ErstelltAmUtc < mailGrenze && x.Status != MailStatus.Offen).ToListAsync(ct);
        var alteNachrichten = await db.Nachrichten.Where(x => x.ErstelltAmUtc < mailGrenze).ToListAsync(ct);
        db.RemoveRange(alteFehler);
        db.RemoveRange(alteMails);
        db.RemoveRange(alteNachrichten);
        await db.SaveChangesAsync(ct);
        return (alteFehler.Count, alteMails.Count, alteNachrichten.Count);
    }
}
