using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TabloSuiteHome.WebApp.Services;
using Xunit;

namespace TabloSuiteHome.Tests;

/// <summary>Preis der App-Karte: holt den aktuellen Preis, fällt bei Fehlern still auf « ohne Zahl » zurück.</summary>
public class TarifAbfrageTests
{
    private sealed class Antworten(Func<HttpResponseMessage> antwort) : HttpMessageHandler
    {
        public int Aufrufe { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Aufrufe++;
            return Task.FromResult(antwort());
        }
    }

    private sealed class Fabrik(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static readonly TabloSuiteApp Skoulou = new() { Id = "skoulou", TarifQuelle = "https://skoulou.test/tarifs/public", TarifEinheit = "par élève et par an" };

    private static (TarifAbfrage, Antworten) Neu(Func<HttpResponseMessage> antwort)
    {
        var h = new Antworten(antwort);
        return (new TarifAbfrage(new Fabrik(h), new MemoryCache(new MemoryCacheOptions()), NullLogger<TarifAbfrage>.Instance), h);
    }

    [Fact]
    public async Task Liest_Den_Preis_Und_Speichert_Ihn_Zwischen()
    {
        var (t, h) = Neu(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"jahrespreisProSchueler":3600,"waehrung":"FCFA"}""", System.Text.Encoding.UTF8, "application/json") });
        Assert.Equal(3600, await t.PreisAsync(Skoulou));
        Assert.Equal(3600, await t.PreisAsync(Skoulou));
        Assert.Equal(1, h.Aufrufe);
        Assert.Equal("3.600", TarifAbfrage.Zahl(3600));
    }

    [Fact]
    public async Task Ohne_Tarif_Oder_Bei_Fehler_Kein_Preis()
    {
        var (leer, _) = Neu(() => new HttpResponseMessage(HttpStatusCode.NoContent));
        Assert.Null(await leer.PreisAsync(Skoulou));
        var (kaputt, _) = Neu(() => throw new HttpRequestException("Kaltstart"));
        Assert.Null(await kaputt.PreisAsync(Skoulou));
        var (fehler, _) = Neu(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await fehler.PreisAsync(Skoulou));
    }

    [Fact]
    public async Task App_Ohne_Preisquelle_Fragt_Nicht()
    {
        var (t, h) = Neu(() => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.Null(await t.PreisAsync(new TabloSuiteApp { Id = "enote" }));
        Assert.Equal(0, h.Aufrufe);
    }

    [Fact]
    public void Pruefung_Verlangt_Https_Und_Einheit()
    {
        var o = new TabloSuiteOptionen { Apps = [new() { Id = "x", Name = "X", Kategorie = "K", Beschreibung = "B", Logo = "l.svg", Url = "https://x.test", TarifQuelle = "http://x.test/t" }] };
        Assert.Contains(o.Pruefe(), f => f.Contains("source du tarif"));
        Assert.Contains(o.Pruefe(), f => f.Contains("unité du tarif"));
    }
}
