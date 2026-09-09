namespace HalloApp.WebApp.Services;

/// <summary>
/// Austauschbare Text-to-Speech-Abstraktion. Standardimplementierung nutzt die Browser-Web-Speech-API
/// (kostenlos, client-seitig). Später kann optional ein OpenAI-TTS-Provider ergänzt werden.
/// </summary>
public interface ITextToSpeechProvider
{
    /// <summary>Liest den angegebenen Text vor.</summary>
    ValueTask SpeakAsync(string text, string lang = "de-DE", double rate = 1.0);

    /// <summary>Stoppt eine laufende Wiedergabe.</summary>
    ValueTask StopAsync();
}
