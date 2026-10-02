using SkoulouApp.Core.DataModels.Finanzen;
using SkoulouApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Tests.Infrastructure;

/// <summary>
/// Erzeugt für Tests <see cref="ApplicationDbContext"/>-Instanzen auf derselben
/// In-Memory-Datenbank. Ersetzt die echte <see cref="IDbContextFactory{TContext}"/>,
/// damit kein SQL Server benötigt wird.
/// </summary>
public sealed class TestApplicationDbContextFactory : IDbContextFactory<ApplicationDbContext>
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TestApplicationDbContextFactory(string? databaseName = null)
        => _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;

    public ApplicationDbContext CreateDbContext() => new InMemoryApplicationDbContext(_options);

    public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());

    private sealed class InMemoryApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            SetComputedBookingNumbers();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            SetComputedBookingNumbers();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void SetComputedBookingNumbers()
        {
            foreach (var entry in ChangeTracker.Entries<FinanzBuchung>().Where(entry => entry.State == EntityState.Added))
            {
                if (entry.Entity.Id == Guid.Empty) entry.Entity.Id = Guid.NewGuid();
                entry.Property(nameof(FinanzBuchung.Buchungsnummer)).CurrentValue = $"B-{entry.Entity.Id:N}".ToUpperInvariant();
            }
        }
    }
}
