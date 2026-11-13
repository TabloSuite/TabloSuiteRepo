using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Skoulou.WebApp.Data;

/// <summary>
/// Nur für « dotnet ef »: baut den Kontext ohne App-Konfiguration (kein Zugriff auf Secrets oder Azure).
/// Die Ziel-Datenbank wird bei « database update » immer ausdrücklich mit --connection angegeben.
/// </summary>
public sealed class SkoulouDbContextDesignFactory : IDesignTimeDbContextFactory<SkoulouDbContext>
{
    public SkoulouDbContext CreateDbContext(string[] args)
    {
        var optionen = new DbContextOptionsBuilder<SkoulouDbContext>()
            .UseSqlServer("Server=localhost;Database=SkoulouDB;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new SkoulouDbContext(optionen);
    }
}
