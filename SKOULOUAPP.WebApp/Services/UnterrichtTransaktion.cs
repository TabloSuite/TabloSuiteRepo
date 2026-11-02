using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SkoulouApp.WebApp.Data;

namespace SkoulouApp.WebApp.Services;

/// <summary>Ein frischer Context je Wiederholungsversuch; dieselbe Transaktion für Prüfung und Schreiben.</summary>
internal static class UnterrichtTransaktion
{
    public static async Task<T> AusfuehrenAsync<T>(IDbContextFactory<ApplicationDbContext> factory,
        Func<ApplicationDbContext, Task<T>> aktion, CancellationToken token)
    {
        await using var strategieContext = await factory.CreateDbContextAsync(token);
        return await strategieContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(token);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var ergebnis = await aktion(db);
            if (transaction is not null) await transaction.CommitAsync(token);
            return ergebnis;
        });
    }
}
