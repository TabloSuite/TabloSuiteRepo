using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ENote.WebApp.Data;

/// <summary>
/// SQL Server behält die Isolationsstufe einer Verbindung, wenn sie in den Pool zurückgeht: nach einer Transaktion mit
/// <c>Serializable</c> (z. B. <c>VertragsService.SynchronisiereAsync</c>) liefen zufällige spätere Abfragen – auch Verkäufe –
/// ebenfalls mit Serializable (Bereichssperren, Deadlocks unter Last; gefunden 18.11.2026 in der 18-Monats-Simulation,
/// nachgewiesen: gleiche SPID kommt mit Stufe 4 aus dem Pool). Dieser Interceptor merkt sich strenge Transaktionen je
/// Verbindung und setzt nach Commit/Rollback auf READ COMMITTED zurück, solange die Verbindung noch offen ist.
/// </summary>
public sealed class IsolationsRuecksetzer : DbTransactionInterceptor
{
    public const string Ruecksetzen = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED;";
    private static readonly ConditionalWeakTable<DbConnection, object> Streng = new();
    private static readonly object Markierung = new();

    private static bool IstStreng(IsolationLevel stufe) => stufe is IsolationLevel.Serializable or IsolationLevel.RepeatableRead or IsolationLevel.Snapshot;

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        if (IstStreng(result.IsolationLevel)) Streng.AddOrUpdate(connection, Markierung);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (IstStreng(result.IsolationLevel)) Streng.AddOrUpdate(connection, Markierung);
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Zuruecksetzen(eventData);
    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Zuruecksetzen(eventData);
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => ZuruecksetzenAsync(eventData, cancellationToken);
    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => ZuruecksetzenAsync(eventData, cancellationToken);

    private static DbConnection? Verbindung(TransactionEndEventData d)
        => d.Context?.Database is { } db && Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.IsRelational(db)
            ? Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetDbConnection(db) : null;

    private static void Zuruecksetzen(TransactionEndEventData d)
    {
        if (Verbindung(d) is not { } c || !Streng.TryGetValue(c, out _)) return;
        Streng.Remove(c);
        if (c.State != ConnectionState.Open) return;
        try { using var cmd = c.CreateCommand(); cmd.CommandText = Ruecksetzen; cmd.ExecuteNonQuery(); }
        catch (DbException) { /* Verbindung defekt: der Pool verwirft sie ohnehin */ }
    }

    private static async Task ZuruecksetzenAsync(TransactionEndEventData d, CancellationToken ct)
    {
        if (Verbindung(d) is not { } c || !Streng.TryGetValue(c, out _)) return;
        Streng.Remove(c);
        if (c.State != ConnectionState.Open) return;
        try { await using var cmd = c.CreateCommand(); cmd.CommandText = Ruecksetzen; await cmd.ExecuteNonQueryAsync(ct); }
        catch (DbException) { /* Verbindung defekt: der Pool verwirft sie ohnehin */ }
    }
}
