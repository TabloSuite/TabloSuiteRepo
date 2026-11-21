using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>Puffer zwischen Serilog und Datenbank (begrenzt; bei Überlauf fallen die ältesten weg).</summary>
public sealed class FehlerProtokollPuffer
{
    private readonly Channel<FehlerProtokoll> kanal = Channel.CreateBounded<FehlerProtokoll>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public bool Schreibe(FehlerProtokoll eintrag) => kanal.Writer.TryWrite(eintrag);

    public ChannelReader<FehlerProtokoll> Leser => kanal.Reader;
}

/// <summary>
/// Serilog-Sink: Ereignisse ab « Error » mit Exception landen gepuffert im Fehlerjournal.
/// Kein DB-Zugriff im Logging-Pfad; Konto und Schule kommen aus den Claims.
/// </summary>
public sealed class FehlerProtokollSink(FehlerProtokollPuffer puffer, IHttpContextAccessor http) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Exception is null || !Relevant(logEvent)) return;
        if (IstVerlasseneVerbindung(logEvent.Exception)) return;
        try
        {
            var kontext = http.HttpContext;
            var user = kontext?.User;
            puffer.Schreibe(new FehlerProtokoll
            {
                ZeitUtc = logEvent.Timestamp.UtcDateTime,
                Stufe = logEvent.Level.ToString(),
                Nachricht = Kuerze(logEvent.RenderMessage(), 4000),
                Typ = Kuerze(logEvent.Exception.GetType().FullName ?? "Exception", 500),
                Details = logEvent.Exception.ToString(),
                Pfad = Kuerze(kontext?.Request.Path.Value, 2000),
                KontoId = Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var k) ? k : null,
                SchuleId = Guid.TryParse(user?.FindFirstValue(SkoulouClaims.Schule), out var s) ? s : null
            });
        }
        catch
        {
            // Logging darf den ursprünglichen Fehler nie verdecken.
        }
    }

    /// <summary>
    /// Ab « Error » – und die Warnung, mit der Blazors ErrorBoundary einen Seitenabsturz meldet (« Une erreur inattendue
    /// s'est produite. L'erreur a été enregistrée. »). Klicktest 21.11.2026: diese Abstürze fehlten im Fehlerjournal,
    /// weil Blazor sie nur als Warning protokolliert.
    /// </summary>
    public static bool Relevant(LogEvent e)
        => e.Level >= LogEventLevel.Error
           || (e.Level == LogEventLevel.Warning && e.Properties.TryGetValue("SourceContext", out var quelle)
               && quelle is ScalarValue { Value: string name } && name.EndsWith(".ErrorBoundary", StringComparison.Ordinal));

    private static string Kuerze(string? text, int max) => text is null ? string.Empty : text.Length > max ? text[..max] : text;

    /// <summary>
    /// Navigation einer schon getrennten Verbindung: wird eine Sitzung auf einem anderen Gerät ersetzt, schickt die
    /// Sitzungsprüfung (60 s) auch die verlassene Verbindung zur Anmeldung (ZugriffUmleitung). Ohne Browser bricht der
    /// JS-Aufruf ab, Blazor meldet « Navigation failed … » und « Unhandled exception in circuit » – je ein Eintrag im
    /// Fehlerjournal pro verlassenem Gerät (Klicktest 20.11.2026: 26 von 34 Einträgen). Kein Fehler der App, nicht protokolliert.
    /// </summary>
    public static bool IstVerlasseneVerbindung(Exception ex)
        => ex is OperationCanceledException or Microsoft.JSInterop.JSDisconnectedException
           && ex.StackTrace?.Contains("RemoteNavigationManager", StringComparison.Ordinal) == true;
}

/// <summary>Schreibt den Puffer im Hintergrund stapelweise in die Datenbank.</summary>
public sealed class FehlerProtokollSchreiber(
    FehlerProtokollPuffer puffer,
    IDbContextFactory<SkoulouDbContext> dbFactory,
    ILogger<FehlerProtokollSchreiber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stapel = new List<FehlerProtokoll>(50);
        try
        {
            while (await puffer.Leser.WaitToReadAsync(stoppingToken))
            {
                stapel.Clear();
                while (stapel.Count < 50 && puffer.Leser.TryRead(out var eintrag)) stapel.Add(eintrag);
                try
                {
                    await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    db.FehlerProtokolle.AddRange(stapel);
                    await db.SaveChangesAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Bewusst nur Warning: der Sink ignoriert Warnungen, sonst entstünde eine Schleife.
                    logger.LogWarning("Fehlerjournal konnte {Anzahl} Einträge nicht speichern: {Meldung}", stapel.Count, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren.
        }
    }
}
