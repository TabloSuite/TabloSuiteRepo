using System.Data;
using ENote.WebApp.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ENote.WebApp.Services;

/// <summary>
/// Vertragssperre je Laden (22.11.2026, ersetzt <c>Serializable</c> in <see cref="VertragsService.SynchronisiereAsync"/>):
/// Abrechnungsperioden anlegen, Formel wechseln, kündigen und Löschung beantragen bzw. zurücknehmen laufen je Laden
/// nacheinander. Die zweite Synchronisierung wartet, bis die erste bestätigt ist, und sieht dann deren Perioden – keine
/// doppelte Rechnung. Vorher sperrte <c>Serializable</c> Schlüsselbereiche des Index <c>IX_NutzungsAbos_SalonId</c>, die
/// über Ladengrenzen reichten: Öffneten alle Läden am Monatsanfang gleichzeitig die Kasse, gab es Deadlocks 1205
/// (Klicktest über zwei Jahre, Befund B-3). Die Anwendungssperre (<c>sp_getapplock</c>) gilt nur für den einen Laden,
/// die Transaktion bleibt READ COMMITTED. Ohne relationale Datenbank (Unit-Tests, InMemory) keine Transaktion.
/// </summary>
internal static class VertragsSperre
{
    public const string BelegtFehler = "L'abonnement est en cours de mise à jour. Réessayez dans un instant.";

    /// <summary>Fehlernummer, wenn die Sperre nicht innerhalb der Wartezeit frei wird.</summary>
    private const int BelegtNummer = 51001;

    /// <summary>
    /// Führt <paramref name="aktion"/> unter der Vertragssperre des Ladens aus (Retry-Strategie der App, frischer Kontext je
    /// Versuch). Die Aktion speichert selbst (<c>SaveChangesAsync</c>); bestätigt wird danach hier. Bei einer Ausnahme wird
    /// alles verworfen. Mails erst nach dem Aufruf senden (nicht in der Aktion), sonst gingen sie bei einem Retry doppelt raus.
    /// </summary>
    public static async Task<T> MitLadenAsync<T>(IDbContextFactory<ApplicationDbContext> factory, Guid salonId,
        Func<ApplicationDbContext, Task<T>> aktion, CancellationToken ct)
    {
        await using var vorbereitung = await factory.CreateDbContextAsync(ct);
        try
        {
            return await vorbereitung.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var transaktion = await SperreAsync(db, salonId, ct);
                var ergebnis = await aktion(db);
                if (transaktion is not null) await transaktion.CommitAsync(ct);
                return ergebnis;
            });
        }
        catch (SqlException ex) when (ex.Number == BelegtNummer)
        {
            throw new InvalidOperationException(BelegtFehler, ex);
        }
    }

    /// <inheritdoc cref="MitLadenAsync{T}"/>
    public static Task MitLadenAsync(IDbContextFactory<ApplicationDbContext> factory, Guid salonId,
        Func<ApplicationDbContext, Task> aktion, CancellationToken ct)
        => MitLadenAsync(factory, salonId, async db => { await aktion(db); return true; }, ct);

    /// <summary>Beginnt die Transaktion (READ COMMITTED) und wartet höchstens 30 s auf die Sperre des Ladens.</summary>
    private static async Task<IDbContextTransaction?> SperreAsync(ApplicationDbContext db, Guid salonId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        // Bei einem Fehler wird die ganze Transaktion verworfen – Savepoints unnötig (mit MARS ohnehin aus).
        db.Database.AutoSavepointsEnabled = false;
        var transaktion = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var ressource = "enote-vertrag-" + salonId.ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {ressource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; IF @r < 0 THROW 51001, N'Vertragssperre belegt', 1;", ct);
        return transaktion;
    }
}
