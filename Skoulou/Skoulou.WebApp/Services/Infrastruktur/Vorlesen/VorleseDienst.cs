using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using System.Xml;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Skoulou.Core.Paedagogik;
using Skoulou.WebApp.Services.Infrastruktur.Medien;

namespace Skoulou.WebApp.Services.Infrastruktur.Vorlesen;

/// <summary>Reine Regeln des Vorlesens: Sprache, Stimme, Tempo (Niveau), SSML, Zwischenspeicher-Schlüssel und Ablageort.</summary>
public static class VorleseRegeln
{
    /// <summary>Wie die Eingabe im Editor (AufgabenAbgleich): längere Texte liest der Browser.</summary>
    public const int MaxZeichen = 2000;

    public const string Format = "audio-24khz-48kbitrate-mono-mp3";
    public const string ContentType = "audio/mpeg";
    public const string Pfadanfang = "/vorlesen/";

    /// <summary>Erhöhen, wenn sich SSML oder Stimmen ändern – alte Dateien werden dann nicht mehr verwendet.</summary>
    private const string Fassung = "v1";

    /// <summary>
    /// Weibliche Neural-Stimmen wie « Vivienne » der gesprochenen Rückmeldung, je Sprache aus <see cref="FragenTypen.Sprachen"/>
    /// (Schlüssel = Sprachkürzel; ein Test prüft, dass jede Sprache eine passende Stimme hat).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Stimmen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = "fr-FR-VivienneMultilingualNeural",
        ["en"] = "en-US-AvaNeural",
        ["de"] = "de-DE-KatjaNeural",
        ["es"] = "es-ES-ElviraNeural",
        ["it"] = "it-IT-ElsaNeural",
        ["pt"] = "pt-PT-RaquelNeural",
        ["ar"] = "ar-SA-ZariyahNeural",
        ["zh"] = "zh-CN-XiaoxiaoNeural"
    };

    /// <summary>Unterstützte Sprache der Aufgabe (genau, sonst gleiches Kürzel, sonst Französisch).</summary>
    public static string Sprache(string? code)
    {
        var sprachen = FragenTypen.Sprachen.Select(s => s.Code).ToList();
        if (string.IsNullOrWhiteSpace(code)) return sprachen[0];
        var c = code.Trim();
        return sprachen.FirstOrDefault(s => s.Equals(c, StringComparison.OrdinalIgnoreCase))
               ?? sprachen.FirstOrDefault(s => c.Length >= 2 && s.StartsWith(c[..2], StringComparison.OrdinalIgnoreCase))
               ?? sprachen[0];
    }

    /// <summary>Stimme zur Sprache; eine gültige Angabe aus « AzureSpeech:Stimmen » hat Vorrang.</summary>
    public static string Stimme(string sprache, IReadOnlyDictionary<string, string>? eigene = null)
    {
        var kuerzel = sprache.Length >= 2 ? sprache[..2] : sprache;
        if (eigene is not null && eigene.TryGetValue(kuerzel, out var s) && IstStimmenName(s)) return s.Trim();
        return Stimmen.TryGetValue(kuerzel, out var std) ? std : Stimmen["fr"];
    }

    /// <summary>Form « xx-XX-NameNeural » (nur Buchstaben, Ziffern, Bindestriche).</summary>
    public static bool IstStimmenName(string? name)
        => name?.Trim() is { Length: > 5 and <= 80 } n && n.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && n.Contains('-');

    /// <summary>Mehrsprachige Stimmen brauchen die Sprache ausdrücklich (sonst raten sie, z. B. « chat » auf Englisch).</summary>
    public static bool IstMehrsprachig(string stimme) => stimme.Contains("Multilingual", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Text für Azure und Schlüssel: Unicode-NFC, Leerraum zu einem Leerzeichen, ohne Steuer- und in XML ungültige Zeichen.
    /// </summary>
    public static string Normalisieren(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        // Erst filtern, dann NFC: ein einzelnes Surrogat ließe Normalize eine Ausnahme werfen.
        var sb = new StringBuilder(text.Length);
        var leer = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { leer = sb.Length > 0; continue; }
            var paar = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            if (!paar && (char.IsControl(c) || !XmlConvert.IsXmlChar(c))) continue;
            if (leer) { sb.Append(' '); leer = false; }
            sb.Append(c);
            if (paar) sb.Append(text[++i]);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Tempo als SSML-Wert (« -35% », « +5% »); 0 = natürlich.</summary>
    public static string Rate(int prozent)
        => (prozent > 0 ? "+" : string.Empty) + prozent.ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>SSML für die REST-Schnittstelle (Text muss normalisiert sein; wird hier maskiert).</summary>
    public static string Ssml(string text, string sprache, string stimme, int prozent)
    {
        var inhalt = SecurityElement.Escape(text);
        if (prozent != 0) inhalt = $"<prosody rate=\"{Rate(prozent)}\">{inhalt}</prosody>";
        if (IstMehrsprachig(stimme)) inhalt = $"<lang xml:lang=\"{SecurityElement.Escape(sprache)}\">{inhalt}</lang>";
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{SecurityElement.Escape(sprache)}\">"
               + $"<voice name=\"{SecurityElement.Escape(stimme)}\">{inhalt}</voice></speak>";
    }

    /// <summary>SHA-256 (64 Hex-Zeichen, klein) über alles, was den Klang bestimmt – auch Niveau und Tempo.</summary>
    public static string Schluessel(string text, string sprache, string stimme, string niveau, bool langsam)
    {
        var roh = string.Join('\n', Fassung, Format, stimme, sprache, SprechNiveaus.Normiert(niveau), langsam ? "langsam" : "normal",
            Rate(SprechNiveaus.Prozent(niveau, langsam)), text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(roh)));
    }

    public static bool IstSchluessel(string? schluessel)
        => schluessel is { Length: 64 } && schluessel.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>Ablage im Medienspeicher – je Schule getrennt (die Auslieferung prüft die Schule des Kontos).</summary>
    public static string Pfad(Guid? schuleId, string schluessel)
        => schuleId is { } s ? $"schulen/{s:N}/vorlesen/{schluessel}" : $"plattform/vorlesen/{schluessel}";

    public static string Adresse(string schluessel) => Pfadanfang + schluessel;

    public static string Endpunkt(string region) => $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";
}

/// <summary>
/// Vorlesen der Audio-Aufgaben mit der Azure-Sprachausgabe (Neural-Stimmen, MP3). Jede Fassung (Text, Sprache, Stimme, Niveau, Tempo)
/// wird je Schule nur einmal erzeugt und im Medienspeicher abgelegt (Name = Hash); der Browser holt sie über
/// <c>/vorlesen/{schluessel}</c>. Ohne Konfiguration, bei zu langem Text, erreichter Grenze oder einem Fehler von Azure bzw.
/// des Speichers liefert der Dienst <c>null</c> – dann liest der Browser wie bisher mit seiner eigenen Stimme.
/// </summary>
public sealed class VorleseDienst(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<SprachausgabeOptionen> optionen,
    IMedienSpeicher speicher,
    IMemoryCache cache,
    TimeProvider zeit,
    ILogger<VorleseDienst> logger) : IDisposable
{
    public const string HttpClientName = "azure-speech";

    /// <summary>Nach einem Fehler so lange gleich die Browser-Stimme (kein Warten auf Azure bei jedem Klick).</summary>
    public static readonly TimeSpan PauseNachFehler = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan BekanntDauer = TimeSpan.FromHours(12);

    private readonly PartitionedRateLimiter<Guid> grenze = PartitionedRateLimiter.Create<Guid, Guid>(konto =>
        RateLimitPartition.GetFixedWindowLimiter(konto, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = RateLimits.VorleseSynthesenJeKontoJeZehnMinuten, Window = TimeSpan.FromMinutes(10), QueueLimit = 0
        }));

    /// <summary>Laufende Erzeugungen je Ablageort: gleichzeitige Klicks warten auf dieselbe Anfrage an Azure.</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> laufend = new();

    private long pauseBisTicks;

    /// <summary>
    /// Adresse der MP3 für diesen Text (vorhanden oder frisch erzeugt) oder <c>null</c> = Browser-Stimme verwenden.
    /// <paramref name="kontoId"/> zählt für die Grenze, <paramref name="schuleId"/> bestimmt den Ablageort.
    /// </summary>
    public async Task<string?> AdresseAsync(Guid kontoId, Guid? schuleId, string? text, string? sprache, string? niveau, bool langsam,
        CancellationToken ct = default)
    {
        var o = optionen.CurrentValue;
        if (!o.IstBereit) return null;
        var sauber = VorleseRegeln.Normalisieren(text);
        if (sauber.Length is 0 or > VorleseRegeln.MaxZeichen) return null;

        var code = VorleseRegeln.Sprache(sprache);
        var stimme = VorleseRegeln.Stimme(code, o.Stimmen);
        var stufe = SprechNiveaus.Normiert(niveau);
        var prozent = SprechNiveaus.Prozent(stufe, langsam);
        var schluessel = VorleseRegeln.Schluessel(sauber, code, stimme, stufe, langsam);
        var pfad = VorleseRegeln.Pfad(schuleId, schluessel);
        var adresse = VorleseRegeln.Adresse(schluessel);

        if (cache.TryGetValue(CacheSchluessel(pfad), out _)) return adresse;
        if (InPause) return null;
        try
        {
            await using var vorhanden = await speicher.OeffnenAsync(pfad, ct);
            if (vorhanden is not null)
            {
                cache.Set(CacheSchluessel(pfad), true, BekanntDauer);
                return adresse;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Vorlesen: Speicher nicht lesbar ({Art}: {Meldung}) – Browser-Stimme.", ex.GetType().Name, ex.Message);
            Pausieren();
            return null;
        }

        using var erlaubnis = grenze.AttemptAcquire(kontoId);
        if (!erlaubnis.IsAcquired)
        {
            logger.LogInformation("Vorlesen: Grenze für Konto {Konto} erreicht – Browser-Stimme.", kontoId);
            return null;
        }

        Lazy<Task<bool>>? eigener = null;
        eigener = new Lazy<Task<bool>>(async () =>
        {
            try { return await ErzeugenAsync(o, pfad, sauber, code, stimme, prozent); }
            finally { laufend.TryRemove(KeyValuePair.Create(pfad, eigener!)); }
        });
        var auftrag = laufend.GetOrAdd(pfad, eigener);
        return await auftrag.Value.WaitAsync(ct) ? adresse : null;
    }

    private bool InPause => zeit.GetUtcNow().UtcTicks < Volatile.Read(ref pauseBisTicks);

    private void Pausieren() => Volatile.Write(ref pauseBisTicks, (zeit.GetUtcNow() + PauseNachFehler).UtcTicks);

    private static string CacheSchluessel(string pfad) => "vorlesen:" + pfad;

    /// <summary>Eine Anfrage an Azure, Ablage im Speicher. Unabhängig vom Klick (andere warten evtl. mit).</summary>
    private async Task<bool> ErzeugenAsync(SprachausgabeOptionen o, string pfad, string text, string sprache, string stimme, int prozent)
    {
        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, VorleseRegeln.Endpunkt(o.RegionNormiert))
            {
                Content = new StringContent(VorleseRegeln.Ssml(text, sprache, stimme, prozent), Encoding.UTF8, "application/ssml+xml")
            };
            anfrage.Headers.Add("Ocp-Apim-Subscription-Key", o.Key);
            anfrage.Headers.Add("X-Microsoft-OutputFormat", VorleseRegeln.Format);
            anfrage.Headers.UserAgent.ParseAdd("SKOULOU");
            using var antwort = await client.SendAsync(anfrage, CancellationToken.None);
            if (!antwort.IsSuccessStatusCode)
            {
                logger.LogWarning("Vorlesen: Azure antwortet {Status} – Browser-Stimme für {Minuten} min.",
                    (int)antwort.StatusCode, PauseNachFehler.TotalMinutes);
                Pausieren();
                return false;
            }
            var mp3 = await antwort.Content.ReadAsByteArrayAsync(CancellationToken.None);
            if (mp3.Length == 0)
            {
                logger.LogWarning("Vorlesen: Azure lieferte keine Audiodaten – Browser-Stimme.");
                Pausieren();
                return false;
            }
            using var strom = new MemoryStream(mp3, writable: false);
            await speicher.SpeichernAsync(pfad, strom, VorleseRegeln.ContentType, CancellationToken.None);
            cache.Set(CacheSchluessel(pfad), true, BekanntDauer);
            return true;
        }
        catch (Exception ex)
        {
            // Zeitüberschreitung, Netz, Speicher: nie die Aufgabe blockieren – der Browser liest vor.
            logger.LogWarning("Vorlesen fehlgeschlagen ({Art}: {Meldung}) – Browser-Stimme.", ex.GetType().Name, ex.Message);
            Pausieren();
            return false;
        }
    }

    public void Dispose() => grenze.Dispose();
}
