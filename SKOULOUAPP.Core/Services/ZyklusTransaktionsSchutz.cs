using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SkoulouApp.Core.Interfaces;

namespace SkoulouApp.Core.Services;

/// <summary>
/// Serialisiert SQL-Server-Zyklusänderungen vor dem Lesen. Transaktionsgebunden,
/// daher auch zwischen Serverinstanzen wirksam und bei Rollback freigegeben.
/// </summary>
public static class ZyklusTransaktionsSchutz
{
    public static async Task SperrenAsync(ISkoulouAppDbContext db, CancellationToken token, string resource = "SKOULOUAPP:Zyklusverwaltung")
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer") return;
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {0},
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @result < 0 THROW 51000, 'Impossible de verrouiller la gestion des cycles. Veuillez réessayer.', 1;
            """, new object[] { resource }, token);
    }

    public static void WiederholungVorbereiten(ISkoulouAppDbContext db, int versuch)
    {
        // SaveChanges akzeptiert Zwischenstände schon vor dem Commit. Nach Rollback
        // dürfen weder diese Zustände noch referenzierte Rechnungs-IDs weiterleben.
        if (versuch > 0)
            db.Database.GetService<ICurrentDbContext>().Context.ChangeTracker.Clear();
    }
}
