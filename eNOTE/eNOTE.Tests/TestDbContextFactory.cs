using ENote.WebApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ENote.Tests;

/// <summary>Erzeugt eine frische InMemory-DbContext-Factory je Test.</summary>
internal static class TestDbContextFactory
{
    public static IDbContextFactory<ApplicationDbContext> Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestFactory(options);
    }

    private sealed class TestFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ApplicationDbContext(options));
    }
}
