namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Gibt abgelaufene Versuche automatisch ab (Examen nach Deadline + 30 s Karenz, Leçons nach der Frist).
/// Läuft alle 15 Sekunden; jeder Lauf ist idempotent (optimistische Sperre auf den Versuchsstatus).
/// </summary>
public sealed class PruefungsAbgabeDienst(PruefungsService pruefungen, ILogger<PruefungsAbgabeDienst> logger) : BackgroundService
{
    public static readonly TimeSpan Takt = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var takt = new PeriodicTimer(Takt);
        do
        {
            try
            {
                var anzahl = await pruefungen.AbgelaufeneAbgebenAsync(stoppingToken);
                if (anzahl > 0) logger.LogInformation("{Anzahl} abgelaufene Versuche automatisch abgegeben.", anzahl);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatische Abgabe fehlgeschlagen.");
            }
        }
        while (await takt.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
