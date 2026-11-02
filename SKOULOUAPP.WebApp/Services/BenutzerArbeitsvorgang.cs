using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Services;
using SkoulouApp.Core.Services.Storage;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

/// <summary>Keine circuitweite Context-Freigabe: jede Ausführung und jeder Retry erhält eigene Instanzen.</summary>
public sealed class BenutzerArbeitsvorgang(IServiceScopeFactory scopes, IDbContextFactory<ApplicationDbContext> factory)
    : IBenutzerArbeitsvorgang
{
    public async Task<T> AusfuehrenAsync<T>(Func<DashboardService, SchoolUserImportService, Task<T>> aktion,
        Func<T, bool> erfolgreich, CancellationToken token)
    {
        await using var strategieDb = await factory.CreateDbContextAsync(token);
        var vorgangsID = Guid.NewGuid();
        var commitBegonnen = false;
        T? erfolgreichesErgebnis = default;
        return await strategieDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Bei verlorener Commit-Antwort zuerst den mitgeschriebenen Beleg prüfen.
            // So bleiben auch einmalige Zugangsdaten des erfolgreichen Versuchs erhalten.
            if (commitBegonnen)
            {
                await using var pruefung = await factory.CreateDbContextAsync(token);
                if (await pruefung.BenutzerVorgangsBestaetigungen.AsNoTracking().AnyAsync(x => x.Id == vorgangsID, token))
                    return erfolgreichesErgebnis!;
                commitBegonnen = false;
            }
            await using var scope = scopes.CreateAsyncScope();
            await using var db = await factory.CreateDbContextAsync(token);
            using var store = new UserStore<AppUser, Role, ApplicationDbContext, Guid, IdentityUserClaim<Guid>, AppUserRole,
                IdentityUserLogin<Guid>, IdentityUserToken<Guid>, IdentityRoleClaim<Guid>>(db);
            using var manager = ActivatorUtilities.CreateInstance<UserManager<AppUser>>(scope.ServiceProvider, store);
            var dashboard = new DashboardService(db, manager, scope.ServiceProvider.GetRequiredService<IMediaStorageService>(),
                scope.ServiceProvider.GetRequiredService<IHostEnvironment>());
            var import = new SchoolUserImportService(db, dashboard);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            await ZyklusTransaktionsSchutz.SperrenAsync(db, token);
            var result = await aktion(dashboard, import);
            if (erfolgreich(result))
            {
                db.BenutzerVorgangsBestaetigungen.Add(new() { Id = vorgangsID, ErstelltAmUtc = DateTime.UtcNow });
                await db.SaveChangesAsync(token);
                erfolgreichesErgebnis = result;
                commitBegonnen = true;
                await transaction.CommitAsync(token);
            }
            return result;
        });
    }
}
