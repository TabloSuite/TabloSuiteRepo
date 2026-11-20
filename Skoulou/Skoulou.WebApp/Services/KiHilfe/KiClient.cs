using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace Skoulou.WebApp.Services.KiHilfe;

/// <summary>Eine Anfrage: großer, gleichbleibender System-Text (zwischengespeichert) und die kurze Nutzernachricht.</summary>
public sealed record KiAnfrage(string SystemText, string NutzerText);

/// <summary>Warum die Antwort endete.</summary>
public enum KiStopp { Fertig, Abgeschnitten, Abgelehnt, Sonstiges }

/// <summary>Antwort des Modells mit Token-Verbrauch (für das Log, ohne Inhalt).</summary>
public sealed record KiRohAntwort(string Text, KiStopp Stopp, long EingabeTokens, long AusgabeTokens, long CacheGelesen, long CacheGeschrieben);

/// <summary>Art eines technischen Fehlers (Meldung an den Nutzer ist immer dieselbe).</summary>
public enum KiFehlerArt { Netzwerk, Zeitueberschreitung, Ueberlastet, Dienst, Konfiguration, Unbekannt }

/// <summary>Der Hilfedienst ist nicht erreichbar oder lehnt die Anfrage technisch ab.</summary>
public sealed class KiDienstFehler(KiFehlerArt art, string beschreibung, Exception? innerer = null)
    : Exception(beschreibung, innerer)
{
    public KiFehlerArt Art { get; } = art;
}

/// <summary>Schnittstelle zum Sprachmodell (in Tests ein Fake – nie echte Aufrufe).</summary>
public interface IKiClient
{
    Task<KiRohAntwort> FrageAsync(KiAnfrage anfrage, CancellationToken ct);
}

/// <summary>
/// Claude über das offizielle Anthropic-SDK (NuGet « Anthropic »). Haiku 5.5: Denken adaptiv, Effort « low » (kurze,
/// schnelle Antworten), keine Vorbelegung der Antwort; der System-Text trägt die Cache-Marke (Wissensbasis zuerst, die Frage
/// danach). Bei « refusal » gibt es für Haiku 5.5 keinen Ersatz auf dem Server – der Aufrufer zeigt eine Meldung.
/// </summary>
public sealed class AnthropicKiClient(IOptions<KiHilfeOptionen> optionen) : IKiClient
{
    private readonly object sperre = new();
    private AnthropicClient? client;

    /// <summary>Anfrage-Parameter (für Tests sichtbar).</summary>
    public static MessageCreateParams BaueParameter(KiAnfrage anfrage, KiHilfeOptionen o) => new()
    {
        Model = o.ModellNormiert,
        MaxTokens = Math.Clamp(o.MaxTokens, 256, 4096),
        Thinking = new ThinkingConfigAdaptive(),
        OutputConfig = new OutputConfig { Effort = Effort.Low },
        System = new List<TextBlockParam>
        {
            new() { Text = anfrage.SystemText, CacheControl = new CacheControlEphemeral() },
        },
        Messages = [new UserMessageParam { Content = anfrage.NutzerText }],
    };

    public async Task<KiRohAntwort> FrageAsync(KiAnfrage anfrage, CancellationToken ct)
    {
        var o = optionen.Value;
        if (!o.IstBereit) throw new KiDienstFehler(KiFehlerArt.Konfiguration, "Kein Schlüssel konfiguriert.");
        try
        {
            var antwort = await Client(o).Messages.Create(BaueParameter(anfrage, o), ct);
            // Inhalt nach Typ lesen (Denkblöcke können vorangehen), nie nach Position.
            var text = string.Join("\n", antwort.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
            var stopp = antwort.StopReason == "refusal" ? KiStopp.Abgelehnt
                : antwort.StopReason == "max_tokens" ? KiStopp.Abgeschnitten
                : antwort.StopReason == "end_turn" || antwort.StopReason == "stop_sequence" ? KiStopp.Fertig
                : KiStopp.Sonstiges;
            var u = antwort.Usage;
            return new KiRohAntwort(text, stopp, u.InputTokens, u.OutputTokens, u.CacheReadInputTokens ?? 0, u.CacheCreationInputTokens ?? 0);
        }
        // Vom spezifischen zum allgemeinen Fehler (wiederholbar: 429, 5xx, Netz; nicht wiederholbar: 4xx).
        catch (AnthropicRateLimitException e) { throw new KiDienstFehler(KiFehlerArt.Ueberlastet, "429", e); }
        catch (AnthropicUnauthorizedException e) { throw new KiDienstFehler(KiFehlerArt.Konfiguration, "401 (Schlüssel ungültig)", e); }
        catch (AnthropicForbiddenException e) { throw new KiDienstFehler(KiFehlerArt.Konfiguration, "403", e); }
        catch (AnthropicNotFoundException e) { throw new KiDienstFehler(KiFehlerArt.Konfiguration, "404 (Modell?)", e); }
        catch (AnthropicBadRequestException e) { throw new KiDienstFehler(KiFehlerArt.Konfiguration, "400", e); }
        catch (Anthropic5xxException e) { throw new KiDienstFehler(KiFehlerArt.Dienst, "5xx", e); }
        catch (AnthropicApiException e) { throw new KiDienstFehler(KiFehlerArt.Dienst, "HTTP-Fehler", e); }
        catch (AnthropicIOException e) { throw new KiDienstFehler(KiFehlerArt.Netzwerk, "Netzwerk", e); }
        catch (HttpRequestException e) { throw new KiDienstFehler(KiFehlerArt.Netzwerk, "Netzwerk", e); }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new KiDienstFehler(KiFehlerArt.Zeitueberschreitung, "Zeitüberschreitung", e);
        }
        catch (AnthropicException e) { throw new KiDienstFehler(KiFehlerArt.Unbekannt, e.GetType().Name, e); }
    }

    /// <summary>Ein Client für die ganze App (HTTP-Verbindungen werden wiederverwendet); Schlüssel nur aus « Ki ».</summary>
    private AnthropicClient Client(KiHilfeOptionen o)
    {
        lock (sperre)
        {
            return client ??= new AnthropicClient(new Anthropic.Core.ClientOptions
            {
                ApiKey = o.AnthropicApiKey!.Trim(),
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(Math.Clamp(o.TimeoutSekunden, 5, 60)),
                // Schlüssel ohne Workspace (« sk-ant-usr… », « sk-ant-svc… ») brauchen den Workspace im Header (20.11.2026)
                ExtraHeaders = string.IsNullOrWhiteSpace(o.WorkspaceId)
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string> { ["anthropic-workspace-id"] = o.WorkspaceId.Trim() },
            });
        }
    }
}
