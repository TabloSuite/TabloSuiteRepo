using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Fehlersuche 21.11.2026: (1) Favoriten-Stern auf zwei Geräten gleichzeitig → duplicate key → Fehlergrenze in der Kasse.
/// (2) Leeres Preisfeld bzw. « 1e6 » wurde still als 0 F gespeichert.
/// </summary>
public class FavoritUndPreisfeldTests
{
    /// <summary>Simuliert den Datenbankfehler des zweiten Geräts (eindeutiger Index) beim Speichern.</summary>
    private sealed class IndexVerletzung : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => eventData.Context!.ChangeTracker.Entries<LeistungFavorit>().Any()
                ? throw new DbUpdateException("IX_LeistungFavoriten_SalonId_LeistungId")
                : ValueTask.FromResult(result);
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> o) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(o);
    }

    [Fact]
    public async Task Gleichzeitiger_Stern_Vom_Anderen_Geraet_Wirft_Nicht()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new IndexVerletzung()).Options;
        var dienst = new KatalogService(new Factory(options), null!, null!);
        await dienst.ToggleFavoritAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());   // darf die Kasse nicht abstürzen lassen
    }

    [Fact]
    public void Preisfeld_Ist_Leer_Bis_Ein_Preis_Eingegeben_Ist()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "eNOTE.WebApp"))) dir = dir.Parent;
        var seite = Path.Combine(dir!.FullName, "eNOTE.WebApp", "Components", "Pages");
        var razor = File.ReadAllText(Path.Combine(seite, "Leistungen.razor"));
        var code = File.ReadAllText(Path.Combine(seite, "Leistungen.razor.cs"));
        Assert.DoesNotContain("@bind=\"bearbeitungsLeistung.Preis\"", razor);
        Assert.Equal(2, razor.Split("@bind=\"PreisFeld\"").Length - 1);
        Assert.Contains("if (preisEingabe is null)", code);
        Assert.Contains("PreisEingabe.MeldungPreisFehlt", code);
        Assert.Contains("0 pour une prestation gratuite", PreisEingabe.MeldungPreisFehlt);
    }
}
