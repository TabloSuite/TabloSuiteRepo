using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.WebApp.Services;

public class VertragsAktualisierung(IServiceScopeFactory scopes, ILogger<VertragsAktualisierung> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                await BesitzerBetreuungsService.ErgaenzeNeueLaedenAsync(db, stoppingToken);
                var ids = await db.Salons.Where(s => s.IstAktiv && s.IstFreigegeben).Select(s => s.Id).ToListAsync(stoppingToken);
                var service = scope.ServiceProvider.GetRequiredService<VertragsService>();
                foreach (var id in ids)
                {
                    try { await service.SynchronisiereAsync(id, stoppingToken); }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogError(ex, "Vertrag für Salon {SalonId} konnte nicht aktualisiert werden", id); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Vertragsaktualisierung fehlgeschlagen"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
