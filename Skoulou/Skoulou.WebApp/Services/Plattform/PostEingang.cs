using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skoulou.Core.Entitaeten;
using Skoulou.WebApp.Data;
using Skoulou.WebApp.Services.Infrastruktur;
using Skoulou.WebApp.Services.Infrastruktur.Mails;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Gemeinsame Adresse contact@tablosuite.com: Postmark liefert eingehende Mails an SKOULOU. Mails zu eNOTE
/// (« eNOTE » im Betreff, nicht « [SKOULOU] ») werden unverändert an den eNOTE-Webhook weitergeleitet
/// (Basic-Auth « enote:&lt;Geheimnis&gt; »); klappt das nicht, bleiben sie im SKOULOU-Posteingang.
/// </summary>
public sealed class MailWeiterleitung(IHttpClientFactory httpClientFactory, IOptionsMonitor<PostmarkOptionen> optionen, ILogger<MailWeiterleitung> logger)
{
    public const string HttpClientName = "mail-weiterleitung";

    /// <summary>Gehört die Mail zu eNOTE? « [SKOULOU] » hat Vorrang (Antwort auf eine SKOULOU-Mail).</summary>
    public static bool GehoertZuEnote(string? betreff)
        => !string.IsNullOrWhiteSpace(betreff)
           && !betreff.Contains(MailAusgangService.AppKennung, StringComparison.OrdinalIgnoreCase)
           && betreff.Contains("enote", StringComparison.OrdinalIgnoreCase);

    /// <summary>Von den Apps selbst verschickte Mails kommen über die gemeinsame Adresse zurück und werden ignoriert.</summary>
    public static bool IstEigeneAbsenderAdresse(string? von, params string?[] eigeneAdressen)
    {
        if (string.IsNullOrWhiteSpace(von) || !System.Net.Mail.MailAddress.TryCreate(von.Trim(), out var adresse)) return false;
        return eigeneAdressen.Any(eigene => !string.IsNullOrWhiteSpace(eigene)
            && string.Equals(adresse.Address, eigene.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Leitet das Postmark-JSON an eNOTE weiter. true = eNOTE hat es angenommen (2xx).</summary>
    public async Task<bool> AnEnoteAsync(string postmarkJson, CancellationToken ct = default)
    {
        var url = optionen.CurrentValue.WeiterleitungEnoteUrl;
        var geheimnis = optionen.CurrentValue.WeiterleitungEnoteGeheimnis;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(geheimnis))
        {
            logger.LogWarning("Mail für eNOTE erhalten, aber keine Weiterleitung konfiguriert – bleibt im SKOULOU-Posteingang.");
            return false;
        }
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(postmarkJson, Encoding.UTF8, "application/json") };
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("enote:" + geheimnis)));
            using var antwort = await client.SendAsync(anfrage, ct);
            if (antwort.IsSuccessStatusCode) return true;
            logger.LogWarning("Weiterleitung an eNOTE abgelehnt: {Status}", (int)antwort.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Weiterleitung an eNOTE fehlgeschlagen – Mail bleibt im SKOULOU-Posteingang: {Meldung}", ex.Message);
            return false;
        }
    }
}

/// <summary>Ergebnis der Verarbeitung einer eingehenden Mail.</summary>
public enum EingangsErgebnis
{
    Ungueltig,
    FremdesZiel,
    EigenerAbsender,
    Weitergeleitet,
    Doppelt,
    Gespeichert
}

/// <summary>Webhook-Logik für /api/postmark/inbound (Prüfen, Weiterleiten, Speichern als Nachricht).</summary>
public sealed class PostEingangService(
    IDbContextFactory<SkoulouDbContext> dbFactory,
    MailWeiterleitung weiterleitung,
    HtmlBereiniger bereiniger,
    IOptionsMonitor<PostmarkOptionen> optionen)
{
    /// <summary>Basic-Auth « skoulou:&lt;Geheimnis&gt; » in konstanter Zeit; ohne Geheimnis immer falsch.</summary>
    public static bool PruefeBasicAuth(string? header, string? geheimnis)
    {
        if (string.IsNullOrWhiteSpace(geheimnis)) return false;
        var erwartet = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("skoulou:" + geheimnis));
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(erwartet), Encoding.UTF8.GetBytes(header ?? string.Empty));
    }

    public async Task<EingangsErgebnis> VerarbeiteAsync(string json, CancellationToken ct = default)
    {
        using var dokument = ParseOderNull(json);
        if (dokument is null || dokument.RootElement.ValueKind != JsonValueKind.Object) return EingangsErgebnis.Ungueltig;
        var wurzel = dokument.RootElement;

        var von = Wert(wurzel, "From");
        var an = Wert(wurzel, "To");
        var id = Wert(wurzel, "MessageID");
        var betreff = Wert(wurzel, "Subject");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(von)) return EingangsErgebnis.Ungueltig;
        // Fremde Empfänger: mit 200 quittieren und ignorieren (sonst wiederholt Postmark endlos).
        if (!an.Contains(SkoulouMarke.KontaktEmail, StringComparison.OrdinalIgnoreCase)) return EingangsErgebnis.FremdesZiel;
        if (MailWeiterleitung.IstEigeneAbsenderAdresse(von, optionen.CurrentValue.FromEmail, SkoulouMarke.KontaktEmail))
            return EingangsErgebnis.EigenerAbsender;
        if (MailWeiterleitung.GehoertZuEnote(betreff) && await weiterleitung.AnEnoteAsync(json, ct))
            return EingangsErgebnis.Weitergeleitet;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var postmarkId = Kuerze(id.Trim(), 100);
        if (await db.Nachrichten.AnyAsync(n => n.PostmarkId == postmarkId, ct)) return EingangsErgebnis.Doppelt;

        var html = Wert(wurzel, "HtmlBody");
        db.Nachrichten.Add(new Nachricht
        {
            Richtung = NachrichtRichtung.Eingang,
            Von = Kuerze(von.Trim(), 256),
            An = Kuerze(an.Trim(), 1000),
            Betreff = Kuerze(string.IsNullOrWhiteSpace(betreff) ? "(sans objet)" : betreff.Trim(), 300),
            TextHtml = html.Length > 0 ? bereiniger.Bereinige(html) : HtmlBereiniger.TextAlsHtml(Wert(wurzel, "TextBody")),
            PostmarkId = postmarkId,
            SchuleId = await SchuleDesAbsendersAsync(db, von, ct)
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return EingangsErgebnis.Doppelt; // gleichzeitige Doppelzustellung (eindeutiger Index)
        }
        return EingangsErgebnis.Gespeichert;
    }

    /// <summary>Automatische Zuordnung: alle Konten dieser Adresse gehören zu genau einer Schule.</summary>
    private static async Task<Guid?> SchuleDesAbsendersAsync(SkoulouDbContext db, string von, CancellationToken ct)
    {
        if (!System.Net.Mail.MailAddress.TryCreate(von.Trim(), out var adresse)) return null;
        var normal = adresse.Address.ToUpperInvariant();
        var schulen = await db.Users.AsNoTracking().Where(k => k.NormalizedEmail == normal && k.SchuleId != null)
            .Select(k => k.SchuleId).Distinct().Take(2).ToListAsync(ct);
        return schulen.Count == 1 ? schulen[0] : null;
    }

    private static JsonDocument? ParseOderNull(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }

    private static string Wert(JsonElement e, string schluessel)
        => e.TryGetProperty(schluessel, out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() ?? string.Empty : string.Empty;

    private static string Kuerze(string text, int max) => text.Length > max ? text[..max] : text;
}
