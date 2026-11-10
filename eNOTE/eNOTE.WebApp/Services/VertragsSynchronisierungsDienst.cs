namespace ENote.WebApp.Services;

/// <summary>
/// Erzeugt regelmäßig die fälligen Abo-Perioden aller abrechenbaren Läden (nach der Probezeit),
/// damit Rechnungen und Kassensperre auch dann stimmen, wenn niemand die Seiten öffnet.
/// Idempotent: <see cref="VertragsService.SynchronisiereAsync"/> legt jede Periode genau einmal an.
/// </summary>
public sealed class VertragsSynchronisierungsDienst(
    IServiceScopeFactory scopeFactory,
    ILogger<VertragsSynchronisierungsDienst> logger) : BackgroundService
{
    public static readonly TimeSpan Intervall = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start nicht verzögern: erst kurz warten, bis die App vollständig läuft.
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        using var takt = new PeriodicTimer(Intervall);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                // Fällig gewordene Tarifänderungen zuerst übernehmen (Anzeige der Preise).
                await scope.ServiceProvider.GetRequiredService<PreisAnpassungService>().WendeFaelligeAnAsync(stoppingToken);
                var vertrag = scope.ServiceProvider.GetRequiredService<VertragsService>();
                foreach (var (salonId, fehler) in await vertrag.SynchronisiereAlleAsync(stoppingToken))
                {
                    logger.LogWarning(fehler, "Abo-Perioden für Laden {SalonId} konnten nicht erzeugt werden.", salonId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Abo-Synchronisierung fehlgeschlagen.");
            }
        }
        while (await takt.WaitForNextTickAsync(stoppingToken));
    }
}
