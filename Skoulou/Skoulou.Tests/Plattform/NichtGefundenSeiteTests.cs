using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Skoulou.WebApp.Services.Infrastruktur;

namespace Skoulou.Tests.Plattform;

/// <summary>Unbekannte Adressen (alter Link, Tippfehler) zeigen die Seite « Page introuvable » (Klick-Simulation S4).</summary>
public class NichtGefundenSeiteTests
{
    /// <summary>Unbekannte Adresse: Seite « Page introuvable » mit Status 404 – nur für Seitenaufrufe (GET, HTML).</summary>
    [Fact]
    public async Task Unbekannte_Adresse_zeigt_die_Seite_introuvable()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.UseNichtGefundenSeite();
        app.MapGet(InfrastrukturRegistrierung.NichtGefundenPfad, () => Results.Content("<h1>Page introuvable</h1>", "text/html"));
        app.MapGet("/bonjour", () => Results.Content("<h1>Bonjour</h1>", "text/html"));
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };

            async Task<(HttpStatusCode Status, string Inhalt)> HoleAsync(HttpMethod methode, string pfad, string accept)
            {
                using var anfrage = new HttpRequestMessage(methode, pfad);
                anfrage.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(accept));
                using var antwort = await client.SendAsync(anfrage);
                return (antwort.StatusCode, await antwort.Content.ReadAsStringAsync());
            }

            var seite = await HoleAsync(HttpMethod.Get, "/ancien/lien", "text/html");
            Assert.Equal(HttpStatusCode.NotFound, seite.Status);
            Assert.Contains("Page introuvable", seite.Inhalt);

            var vorhanden = await HoleAsync(HttpMethod.Get, "/bonjour", "text/html");
            Assert.Equal(HttpStatusCode.OK, vorhanden.Status);
            Assert.Contains("Bonjour", vorhanden.Inhalt);

            var bild = await HoleAsync(HttpMethod.Get, "/medias/absent.png", "image/png");
            Assert.Equal(HttpStatusCode.NotFound, bild.Status);
            Assert.Equal(string.Empty, bild.Inhalt);

            var formular = await HoleAsync(HttpMethod.Post, "/ancien/lien", "text/html");
            Assert.Equal(HttpStatusCode.NotFound, formular.Status);
            Assert.Equal(string.Empty, formular.Inhalt);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
