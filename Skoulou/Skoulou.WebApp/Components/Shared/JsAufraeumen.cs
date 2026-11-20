using Microsoft.JSInterop;

namespace Skoulou.WebApp.Components.Shared;

/// <summary>
/// JS-Aufräumen beim Entsorgen einer Komponente (z. B. « skCloserEntfernen »). Ist die Verbindung schon weg – Browser
/// geschlossen, Sitzung auf einem anderen Gerät ersetzt –, scheitert der Aufruf mit JSDisconnectedException oder
/// TaskCanceledException. Das ist kein Fehler: vorher landete jede solche Entsorgung als « Unhandled exception in circuit »
/// im Fehlerjournal (Klicktest 20.11.2026, AuswahlFeld.DisposeAsync).
/// </summary>
public static class JsAufraeumen
{
    public static async Task VersuchenAsync(IJSRuntime js, string funktion, params object?[] argumente)
    {
        try { await js.InvokeVoidAsync(funktion, argumente); }
        catch (Exception ex) when (IstVerbindungsende(ex)) { }
    }

    /// <summary>Fehler, die nur bedeuten, dass der Browser nicht mehr erreichbar ist (oder noch nicht vorgerendert verbunden).</summary>
    public static bool IstVerbindungsende(Exception ex)
        => ex is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException;
}
