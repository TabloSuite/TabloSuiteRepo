using HalloApp.Core.Services;
using HalloApp.Tests.Infrastructure;
using Xunit;

namespace HalloApp.Tests.Services;

public class AnkuendigungServiceTests
{
    [Fact]
    public async Task ErstellenUndAblauf_FiltertVergangeneAnkuendigungen()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new AnkuendigungService(db);
        var now = DateTime.UtcNow;

        var invalid = await service.ErstellenAsync("Abgelaufen", "<p>Inhalt</p>", now, now);
        Assert.False(invalid.Succeeded);
        Assert.True((await service.ErstellenAsync("Hinweis", "<p>Aktueller Hinweis</p><script>alert(1)</script>", now.AddMinutes(1), now)).Succeeded);
        Assert.Single(await service.GetAktiveAsync(now));
        Assert.DoesNotContain("script", (await service.GetAlleAsync())[0].InhaltHtml);
        Assert.Empty(await service.GetAktiveAsync(now.AddMinutes(1)));
    }

    [Fact]
    public async Task Loeschen_EntferntAnkuendigungVorAblauf()
    {
        using var db = TestDbContext.CreateInMemory();
        var service = new AnkuendigungService(db);
        var now = DateTime.UtcNow;
        await service.ErstellenAsync("Hinweis", "Wichtiger Hinweis", now.AddDays(1), now);
        var id = (await service.GetAktiveAsync(now)).Single().Id;

        Assert.True(await service.LoeschenAsync(id));
        Assert.False(await service.LoeschenAsync(id));
        Assert.Empty(await service.GetAktiveAsync(now));
    }
}
