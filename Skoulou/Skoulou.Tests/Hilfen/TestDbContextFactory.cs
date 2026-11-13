using Microsoft.EntityFrameworkCore;
using Skoulou.WebApp.Data;

namespace Skoulou.Tests.Hilfen;

/// <summary>Frische InMemory-DbContext-Factory je Test (Unique-Indizes werden hier NICHT geprüft – dafür die SQL-Tests).</summary>
internal static class TestDbContextFactory
{
    public static IDbContextFactory<SkoulouDbContext> Create(string? name = null)
    {
        var options = new DbContextOptionsBuilder<SkoulouDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new Fabrik(options);
    }

    private sealed class Fabrik(DbContextOptions<SkoulouDbContext> options) : IDbContextFactory<SkoulouDbContext>
    {
        public SkoulouDbContext CreateDbContext() => new(options);

        public Task<SkoulouDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SkoulouDbContext(options));
    }
}
