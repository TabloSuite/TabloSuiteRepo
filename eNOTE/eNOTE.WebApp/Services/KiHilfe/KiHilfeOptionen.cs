namespace ENote.WebApp.Services.KiHilfe;

/// <summary>
/// Abschnitt « Ki » (KI-Hilfe « Poser une question », Nutzerwunsch 19.11.2026). Schlüssel nur in den App-Einstellungen
/// (Azure: <c>Ki__AnthropicApiKey</c>) bzw. User-Secrets, nie im Repo und nie im Log. Ohne Schlüssel ist die Funktion
/// unsichtbar (kein Menüeintrag, kein Fehler).
/// </summary>
public sealed class KiHilfeOptionen
{
    public const string Abschnitt = "Ki";
    public const string StandardModell = "claude-haiku-5-5";

    /// <summary>Längste erlaubte Frage in Zeichen.</summary>
    public const int MaxFrageZeichen = 500;

    public string? AnthropicApiKey { get; set; }

    /// <summary>Modell-ID (Standard Claude Haiku 5.5).</summary>
    public string? Modell { get; set; } = StandardModell;

    /// <summary>
    /// Für alle Rollen freischalten (Azure: <c>Ki__AlleRollen=true</c>). Standard aus: zunächst nur Administrateurs und
    /// Responsables (Nutzer 19.11.2026: « nicht limitieren und zunächst nur für Admins und POs »). Keine Fragen-Grenzen.
    /// </summary>
    public bool AlleRollen { get; set; }

    /// <summary>
    /// Obergrenze der Antwort einschließlich Denken (Haiku 5.5 denkt adaptiv, das zählt mit). Die Antwort selbst ist kurz
    /// (höchstens 120 Wörter, Effort « low »); das Modell sieht diese Grenze nicht, sie kostet nur, wenn sie gebraucht wird.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>Höchstdauer eines Versuchs in Sekunden (ein Wiederholversuch).</summary>
    public int TimeoutSekunden { get; set; } = 20;

    public bool IstBereit => !string.IsNullOrWhiteSpace(AnthropicApiKey);

    public string ModellNormiert => string.IsNullOrWhiteSpace(Modell) ? StandardModell : Modell.Trim();
}
