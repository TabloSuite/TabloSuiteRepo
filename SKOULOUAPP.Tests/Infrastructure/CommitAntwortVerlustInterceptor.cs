using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SkoulouApp.Tests.Infrastructure;

/// <summary>Simuliert einen Transportfehler NACH einem tatsächlich erfolgreichen Commit.</summary>
public sealed class CommitAntwortVerlustInterceptor : DbTransactionInterceptor
{
    public bool NaechsteAntwortVerlieren { get; set; }
    public int VerloreneAntworten { get; private set; }

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (NaechsteAntwortVerlieren)
        {
            NaechsteAntwortVerlieren = false;
            VerloreneAntworten++;
            throw new TimeoutException("Simulierter Verlust der erfolgreichen Commit-Antwort.");
        }
        return Task.CompletedTask;
    }
}
