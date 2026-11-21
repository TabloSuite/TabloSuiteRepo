using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;

namespace TabloSuiteHome.WebApp.Services;

/// <summary>
/// Holt den aktuellen Standardpreis einer App über ihren öffentlichen Endpunkt (z. B. SKOULOU « /tarifs/public »),
/// damit tablosuite.com nie einen veralteten Preis zeigt. Zwischengespeichert (30 min, nach Fehlern 2 min);
/// antwortet die App nicht (z. B. Kaltstart), zeigt die Karte nur den Hinweis ohne Zahl.
/// Seit 19.11.2026 liefert SKOULOU den Preis je Schüler und Monat (« monatspreisProSchueler »); eine ältere Antwort
/// ohne dieses Feld (nur « jahrespreisProSchueler ») zählt als « kein Preis », damit nie ein Jahrespreis als Monatspreis erscheint.
/// </summary>
public sealed class TarifAbfrage(IHttpClientFactory clients, IMemoryCache cache, ILogger<TarifAbfrage> logger)
{
    public const string Client = "tarife";

    /// <summary>SKOULOU liefert « monatspreisProSchueler », eNOTE (seit 21.11.2026) « monatspreis » je Laden und Monat.</summary>
    private sealed record Antwort(long? MonatspreisProSchueler, long? Monatspreis, string? Waehrung)
    {
        public long? Preis => MonatspreisProSchueler ?? Monatspreis;
    }

    public async Task<long?> PreisAsync(TabloSuiteApp app, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(app.TarifQuelle)) return null;
        var schluessel = "tarif:" + app.TarifQuelle;
        if (cache.TryGetValue(schluessel, out long? preis)) return preis;
        try
        {
            using var antwort = await clients.CreateClient(Client).GetAsync(app.TarifQuelle, ct);
            preis = antwort.IsSuccessStatusCode && antwort.StatusCode != System.Net.HttpStatusCode.NoContent
                ? (await antwort.Content.ReadFromJsonAsync<Antwort>(ct))?.Preis
                : null;
            if (preis is not > 0) preis = null;
            cache.Set(schluessel, preis, preis is null ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(30));
            return preis;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Tarif de {App} non disponible : {Meldung}", app.Id, ex.Message);
            cache.Set(schluessel, (long?)null, TimeSpan.FromMinutes(2));
            return null;
        }
    }

    private static readonly CultureInfo Kultur = Erzeuge();

    private static CultureInfo Erzeuge()
    {
        var k = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        k.NumberFormat.NumberGroupSeparator = ".";
        return k;
    }

    /// <summary>« 5.000 » – Tausenderpunkt wie in den Apps.</summary>
    public static string Zahl(long wert) => wert.ToString("N0", Kultur);
}
