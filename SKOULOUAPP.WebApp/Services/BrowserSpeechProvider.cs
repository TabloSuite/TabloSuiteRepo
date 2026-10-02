using Microsoft.JSInterop;

namespace SkoulouApp.WebApp.Services;

/// <summary>
/// TTS-Provider auf Basis der Browser-Web-Speech-API (window.SKOULOUAPPTts, definiert in js/tts.js).
/// Kostenlos und komplett client-seitig. Deutsche Stimmenverfügbarkeit ist geräteabhängig.
/// </summary>
public sealed class BrowserSpeechProvider(IJSRuntime jsRuntime) : ITextToSpeechProvider
{
    public async ValueTask SpeakAsync(string text, string lang = "de-DE", double rate = 1.0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            await jsRuntime.InvokeVoidAsync("SKOULOUAPPTts.speak", text, lang, rate);
        }
        catch (JSDisconnectedException)
        {
            // Client hat die Verbindung getrennt – ignorieren.
        }
    }

    public async ValueTask StopAsync()
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("SKOULOUAPPTts.stop");
        }
        catch (JSDisconnectedException)
        {
            // ignorieren
        }
    }
}
