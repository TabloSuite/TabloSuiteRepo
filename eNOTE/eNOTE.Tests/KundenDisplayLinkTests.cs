using System.Security.Claims;
using ENote.Core.DataModels.Identity;
using ENote.Core.DataModels.Kasse;
using ENote.Core.DataModels.Salons;
using ENote.WebApp.Data;
using ENote.WebApp.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ENote.Tests;

/// <summary>
/// Nutzerentscheidung 22.11.2026 (Frage 6 des Klicktests: Link nach zwei Jahren noch gültig): Ein neuer Écran-client-Link
/// entwertet den alten. Gültig ist nur der Link des Geräts, an dem die jüngste Kasse des Ladens läuft bzw. zuletzt lief;
/// ein Display mit altem Link zeigt « Ce lien n'est plus valide », und der Hub lässt es nicht mehr abonnieren.
/// </summary>
public class KundenDisplayLinkTests
{
    private static readonly string TabletA = Guid.NewGuid().ToString("N");
    private static readonly string TabletB = Guid.NewGuid().ToString("N");
    private static readonly DateTime Tag1 = new(2027, 3, 1, 7, 30, 0, DateTimeKind.Utc);

    private static async Task<(IDbContextFactory<ApplicationDbContext> F, Guid Laden, Guid Kassierer)> WeltAsync()
    {
        var factory = TestDbContextFactory.Create();
        var laden = new Salon { Name = "Boutique Écran" };
        var kassierer = new AppUser { Id = Guid.NewGuid(), UserName = "awa" };
        await using var db = await factory.CreateDbContextAsync();
        db.Salons.Add(laden);
        db.Users.Add(kassierer);
        await db.SaveChangesAsync();
        return (factory, laden.Id, kassierer.Id);
    }

    private static async Task<KassenSitzung> KasseAsync(IDbContextFactory<ApplicationDbContext> f, Guid laden, Guid kassierer, DateTime start, string? geraet)
    {
        await using var db = await f.CreateDbContextAsync();
        var sitzung = new KassenSitzung { SalonId = laden, KassiererId = kassierer, GestartetUtc = start, GeraetSchluessel = geraet };
        db.KassenSitzungen.Add(sitzung);
        await db.SaveChangesAsync();
        return sitzung;
    }

    private static Task<bool> GiltAsync(IDbContextFactory<ApplicationDbContext> f, Guid laden, string? geraet)
        => KundenDisplayLink.IstGueltigAsync(f, laden, geraet);

    [Fact]
    public async Task Nur_Der_Link_Des_Aktuellen_Kassengeraets_Gilt()
    {
        var (f, laden, kassierer) = await WeltAsync();
        Assert.False(await GiltAsync(f, laden, TabletA));                 // noch nie eine Kasse

        var sitzung = await KasseAsync(f, laden, kassierer, Tag1, TabletA);
        Assert.True(await GiltAsync(f, laden, TabletA));
        Assert.True(await GiltAsync(f, laden, TabletA.ToUpperInvariant()));
        Assert.False(await GiltAsync(f, laden, TabletB));
        Assert.False(await GiltAsync(f, laden, "kein-schluessel"));
        Assert.False(await GiltAsync(f, Guid.NewGuid(), TabletA));      // anderer Laden

        // « Reprendre la caisse ici » auf Tablet B: der Link von A gilt nicht mehr, der neue von B schon.
        await using (var db = await f.CreateDbContextAsync())
        {
            (await db.KassenSitzungen.SingleAsync(k => k.Id == sitzung.Id)).GeraetSchluessel = TabletB;
            await db.SaveChangesAsync();
        }
        Assert.False(await GiltAsync(f, laden, TabletA));
        Assert.True(await GiltAsync(f, laden, TabletB));

        // Nach der Clôture bleibt das Display von B verbunden (« aussi les jours suivants »).
        await using (var db = await f.CreateDbContextAsync())
        {
            var k = await db.KassenSitzungen.SingleAsync(x => x.Id == sitzung.Id);
            k.Status = KassenSitzungStatus.Abgeschlossen;
            k.AbgeschlossenUtc = Tag1.AddHours(11);
            await db.SaveChangesAsync();
        }
        Assert.True(await GiltAsync(f, laden, TabletB));

        // Am nächsten Tag öffnet Tablet A die Kasse: jetzt gilt wieder nur der Link von A.
        await KasseAsync(f, laden, kassierer, Tag1.AddDays(1), TabletA);
        Assert.True(await GiltAsync(f, laden, TabletA));
        Assert.False(await GiltAsync(f, laden, TabletB));
    }

    private sealed class Aufrufer(string id) : HubCallerContext
    {
        public override string ConnectionId { get; } = id;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class Gruppen : IGroupManager
    {
        public readonly List<(string Verbindung, string Gruppe)> Beitritte = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Beitritte.Add((connectionId, groupName));
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task Hub_Laesst_Einen_Alten_Link_Nicht_Mehr_Abonnieren()
    {
        var (f, laden, kassierer) = await WeltAsync();
        await KasseAsync(f, laden, kassierer, Tag1, TabletA);
        await KasseAsync(f, laden, kassierer, Tag1.AddDays(1), TabletB);
        var gruppen = new Gruppen();
        using var hub = new UmsatzHub(f) { Context = new Aufrufer("verbindung-1"), Groups = gruppen };

        Assert.False(await hub.AbonniereGeraetDisplay(laden.ToString(), TabletA));   // alter Link
        Assert.False(await hub.AbonniereGeraetDisplay("kein-laden", TabletB));
        Assert.False(await hub.AbonniereGeraetDisplay(Guid.NewGuid().ToString(), TabletB));
        Assert.Empty(gruppen.Beitritte);

        Assert.True(await hub.AbonniereGeraetDisplay(laden.ToString(), TabletB));     // aktueller Link
        Assert.Equal(("verbindung-1", UmsatzHub.GeraetDisplayGruppenName(TabletB)), Assert.Single(gruppen.Beitritte));
    }

    [Fact]
    public void Display_Zeigt_Die_Franzoesische_Meldung_Und_Prueft_Laufend()
    {
        Assert.Equal("Ce lien n'est plus valide", KundenDisplayLink.UngueltigTitel);
        Assert.StartsWith("Demandez le nouveau lien en caisse", KundenDisplayLink.UngueltigText);

        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !Directory.Exists(Path.Combine(ordner.FullName, "eNOTE", "eNOTE.WebApp"))) ordner = ordner.Parent;
        var seiten = Path.Combine(ordner!.FullName, "eNOTE", "eNOTE.WebApp", "Components", "Pages");
        var razor = File.ReadAllText(Path.Combine(seiten, "Display.razor"));
        Assert.Contains("KundenDisplayLink.UngueltigTitel", razor);
        Assert.Contains("KundenDisplayLink.UngueltigText", razor);
        var code = File.ReadAllText(Path.Combine(seiten, "Display.razor.cs"));
        Assert.Contains("signal.KassenGeraetGewechselt += GeraetGewechselt;", code);
        Assert.Contains("KundenDisplayLink.PruefIntervall", code);
        Assert.Contains("InvokeAsync<bool>(\"AbonniereGeraetDisplay\", SalonId.ToString(), Geraet)", code);
    }
}
