using HalloApp.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Tests.Infrastructure;

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

    public ApplicationDbContext CreateDbContext() => new(_options);

    public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}
