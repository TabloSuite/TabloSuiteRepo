using SkoulouApp.Core.Services;
using SkoulouApp.Tests.Infrastructure;
using SkoulouApp.WebApp.Data;
using Xunit;

namespace SkoulouApp.Tests.Services;

public class FormelFavoritServiceTests
{
    [Fact]
    public async Task HinzufuegenAsync_Und_LadeAsync_SpeichertProBenutzer()
    {
        var factory = new TestApplicationDbContextFactory();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var seedContext = factory.CreateDbContext())
        {
            var service = new FormelFavoritService(seedContext);
            await service.HinzufuegenAsync(userA, "\\frac{a}{b}", "Fraction", "\\frac{a}{b}");
            await service.HinzufuegenAsync(userB, "x^{2}", "Carré", "x^{2}");
        }

        await using var verifyContext = factory.CreateDbContext();
        var verifyService = new FormelFavoritService(verifyContext);

        var resultA = await verifyService.LadeAsync(userA);
        var resultB = await verifyService.LadeAsync(userB);

        Assert.Single(resultA);
        Assert.Equal("\\frac{a}{b}", resultA[0].Latex);
        Assert.Single(resultB);
        Assert.Equal("x^{2}", resultB[0].Latex);
    }

    [Fact]
    public async Task HinzufuegenAsync_DoppeltLatex_AktualisiertStattZuDuplizieren()
    {
        var factory = new TestApplicationDbContextFactory();
        var userId = Guid.NewGuid();

        await using (var context = factory.CreateDbContext())
        {
            var service = new FormelFavoritService(context);
            await service.HinzufuegenAsync(userId, "x^{2}", "Carré", "x^{2}");
            await service.HinzufuegenAsync(userId, "x^{2}", "Carré modifié", "x^{2}");
        }

        await using var verifyContext = factory.CreateDbContext();
        var verifyService = new FormelFavoritService(verifyContext);
        var favorites = await verifyService.LadeAsync(userId);

        Assert.Single(favorites);
        Assert.Equal("Carré modifié", favorites[0].Bezeichnung);
    }

    [Fact]
    public async Task EntfernenAsync_EntferntFavorit()
    {
        var factory = new TestApplicationDbContextFactory();
        var userId = Guid.NewGuid();

        await using (var context = factory.CreateDbContext())
        {
            var service = new FormelFavoritService(context);
            await service.HinzufuegenAsync(userId, "\\sqrt{x}", "Racine", "\\sqrt{x}");
        }

        await using (var deleteContext = factory.CreateDbContext())
        {
            var service = new FormelFavoritService(deleteContext);
            var removed = await service.EntfernenAsync(userId, "\\sqrt{x}");
            Assert.True(removed);
        }

        await using var verifyContext = factory.CreateDbContext();
        var verifyService = new FormelFavoritService(verifyContext);
        var favorites = await verifyService.LadeAsync(userId);

        Assert.Empty(favorites);
    }
}
