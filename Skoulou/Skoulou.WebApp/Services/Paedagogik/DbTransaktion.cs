using Microsoft.EntityFrameworkCore;
using Skoulou.WebApp.Data;

namespace Skoulou.WebApp.Services.Paedagogik;

/// <summary>
/// Mehrschrittige Schreibvorgänge in einer Transaktion – über die Ausführungsstrategie (Wiederholung bei
/// vorübergehenden SQL-Fehlern, dann komplett neu). InMemory (Tests) kennt keine Transaktionen.
/// </summary>
internal static class DbTransaktion
{
    public static async Task<T> AusfuehrenAsync<T>(SkoulouDbContext db, Func<Task<T>> aktion, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return await aktion();
        var strategie = db.Database.CreateExecutionStrategy();
        return await strategie.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var ergebnis = await aktion();
            await tx.CommitAsync(ct);
            return ergebnis;
        });
    }

    public static Task AusfuehrenAsync(SkoulouDbContext db, Func<Task> aktion, CancellationToken ct)
        => AusfuehrenAsync(db, async () => { await aktion(); return true; }, ct);

    /// <summary>Verletzung eines Unique-Index (zwei gleichzeitige Anfragen)?</summary>
    public static bool IstEindeutigkeitsFehler(DbUpdateException ex)
        => ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
