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

    private static readonly TabloSuiteApp Skoulou = new() { Id = "skoulou", TarifQuelle = "https://skoulou.test/tarifs/public", TarifEinheit = "/ élève / mois", TarifJahrMonate = 10 };

    private static (TarifAbfrage, Antworten) Neu(Func<HttpResponseMessage> antwort)
    {
        var h = new Antworten(antwort);
        return (new TarifAbfrage(new Fabrik(h), new MemoryCache(new MemoryCacheOptions()), NullLogger<TarifAbfrage>.Instance), h);
    }

    private static HttpResponseMessage Json(string inhalt)
        => new(HttpStatusCode.OK) { Content = new StringContent(inhalt, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Liest_Den_Monatspreis_Und_Speichert_Ihn_Zwischen()
    {
        // 19.11.2026: SKOULOU liefert den Preis je Schüler und Monat (Karte « 500 FCFA / élève / mois »).
        var (t, h) = Neu(() => Json("""{"monatspreisProSchueler":500,"waehrung":"FCFA"}"""));
        Assert.Equal(500, await t.PreisAsync(Skoulou));
        Assert.Equal(500, await t.PreisAsync(Skoulou));
        Assert.Equal(1, h.Aufrufe);
        Assert.Equal("500", TarifAbfrage.Zahl(500));
        Assert.Equal("5.000", TarifAbfrage.Zahl(500 * Skoulou.TarifJahrMonate!.Value));
    }

    /// <summary>21.11.2026: eNOTE liefert « monatspreis » je Laden und Monat (auf tablosuite.com fehlte der eNOTE-Preis).</summary>
    [Fact]
    public async Task Liest_Den_Monatspreis_Von_ENOTE()
    {
        var enote = new TabloSuiteApp { Id = "enote", TarifQuelle = "https://enote.test/tarifs/public", TarifEinheit = "/ établissement / mois" };
        var (t, _) = Neu(() => Json("""{"monatspreis":2500,"waehrung":"FCFA"}"""));
        Assert.Equal(2500, await t.PreisAsync(enote));
        Assert.Equal("2.500", TarifAbfrage.Zahl(2500));
    }

    /// <summary>Der echte Katalog: beide Apps haben eine Preisquelle, SKOULOU nennt die Emplois du temps.</summary>
    [Fact]
    public void Katalog_eNOTE_Mit_Preis_SKOULOU_Mit_Emplois_Du_Temps()
    {
        var ordner = new DirectoryInfo(AppContext.BaseDirectory);
        while (ordner is not null && !File.Exists(Path.Combine(ordner.FullName, "TabloSuiteHome", "TabloSuiteHome.WebApp", "appsettings.json"))) ordner = ordner.Parent;
        Assert.NotNull(ordner);
        var json = File.ReadAllText(Path.Combine(ordner!.FullName, "TabloSuiteHome", "TabloSuiteHome.WebApp", "appsettings.json"));
        Assert.Contains("\"TarifQuelle\": \"https://enote.tablosuite.com/tarifs/public\"", json);
        Assert.Contains("\"TarifQuelle\": \"https://skoulou.tablosuite.com/tarifs/public\"", json);
        Assert.Contains("Emplois du temps", json);
    }

    [Fact]
    public async Task Alte_Antwort_Mit_Jahrespreis_Gilt_Als_Kein_Preis()
    {
        // Noch nicht neu verteiltes SKOULOU: kein Jahrespreis als Monatspreis auf der Karte, nur der Hinweis.
        var (t, _) = Neu(() => Json("""{"jahrespreisProSchueler":3600,"waehrung":"FCFA"}"""));
        Assert.Null(await t.PreisAsync(Skoulou));
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
        o.Apps[0].TarifJahrMonate = 13;
        Assert.Contains(o.Pruefe(), f => f.Contains("nombre de mois"));
    }
}
