namespace ENote.WebApp.Services;

/// <summary>
/// Zentraler Toast-Benachrichtigungsdienst (Erfolg/Fehler/Info). Meldungen blenden sich
/// automatisch aus (Fehler bleiben länger stehen); eine neue Meldung ersetzt die alte.
/// </summary>
public class ToastService
{
    public static readonly TimeSpan DauerNormal = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan DauerFehler = TimeSpan.FromSeconds(7);

    private int version;

    public event Action? OnChange;

    public string? Nachricht { get; private set; }
    public ToastArt Art { get; private set; } = ToastArt.Info;
    public bool Sichtbar { get; private set; }

    public void ZeigeErfolg(string nachricht) => Zeige(nachricht, ToastArt.Erfolg);
    public void ZeigeFehler(string nachricht) => Zeige(nachricht, ToastArt.Fehler);
    public void ZeigeInfo(string nachricht) => Zeige(nachricht, ToastArt.Info);

    private void Zeige(string nachricht, ToastArt art)
    {
        Nachricht = nachricht;
        Art = art;
        Sichtbar = true;
        var meineVersion = Interlocked.Increment(ref version);
        OnChange?.Invoke();
        _ = BlendeSpaeterAusAsync(meineVersion, art == ToastArt.Fehler ? DauerFehler : DauerNormal);
    }

    private async Task BlendeSpaeterAusAsync(int meineVersion, TimeSpan dauer)
    {
        await Task.Delay(dauer);
        if (meineVersion == Volatile.Read(ref version) && Sichtbar)
        {
            Verbergen();
        }
    }

    public void Verbergen()
    {
        Sichtbar = false;
        OnChange?.Invoke();
    }
}

public enum ToastArt
{
    Info,
    Erfolg,
    Fehler
}
