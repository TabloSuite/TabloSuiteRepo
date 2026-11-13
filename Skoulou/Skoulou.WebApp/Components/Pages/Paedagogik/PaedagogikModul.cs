using Microsoft.JSInterop;

namespace Skoulou.WebApp.Components.Pages.Paedagogik;

/// <summary>
/// Zugang zum JS-Modul « js/paedagogik.js » (Editor, Formeln, Hochladen, Rückmeldung, Uhr) – einmal je
/// Verbindung importiert. Fehler durch eine getrennte Verbindung werden geschluckt.
/// </summary>
public sealed class PaedagogikModul(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? modul;

    private Task<IJSObjectReference> Modul => modul ??= js.InvokeAsync<IJSObjectReference>("import", "./js/paedagogik.js").AsTask();

    public async Task<T?> AufrufAsync<T>(string funktion, params object?[] argumente)
    {
        try
        {
            return await (await Modul).InvokeAsync<T>(funktion, argumente);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
            return default;
        }
    }

    public async Task AufrufAsync(string funktion, params object?[] argumente)
    {
        try
        {
            await (await Modul).InvokeVoidAsync(funktion, argumente);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (modul is { IsCompletedSuccessfully: true })
        {
            try { await modul.Result.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
