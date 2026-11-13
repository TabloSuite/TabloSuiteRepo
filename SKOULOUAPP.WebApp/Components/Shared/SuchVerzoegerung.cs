namespace SkoulouApp.WebApp.Components.Shared;

/// <summary>
/// Führt eine Suche erst nach einer kurzen Tipp-Pause aus (Standard 350 ms), damit nicht
/// bei jedem Buchstaben eine Datenbankabfrage läuft. Eine neue Eingabe verwirft die wartende.
/// </summary>
public sealed class SuchVerzoegerung : IDisposable
{
    private CancellationTokenSource? wartend;

    public int Millisekunden { get; init; } = 350;

    public async Task AusfuehrenAsync(Func<Task> aktion)
    {
        wartend?.Cancel();
        var eigene = wartend = new CancellationTokenSource();
        try
        {
            await Task.Delay(Millisekunden, eigene.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (!eigene.IsCancellationRequested)
        {
            await aktion();
        }
    }

    public void Dispose() => wartend?.Cancel();
}
