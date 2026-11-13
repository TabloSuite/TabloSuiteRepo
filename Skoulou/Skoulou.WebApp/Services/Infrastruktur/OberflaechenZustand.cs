namespace Skoulou.WebApp.Services.Infrastruktur;

/// <summary>
/// Toast-Meldungen (Erfolg/Fehler/Info) je Circuit. Meldungen blenden sich aus (Fehler bleiben länger);
/// eine neue Meldung ersetzt die alte.
/// </summary>
public sealed class ToastService
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
        var meine = Interlocked.Increment(ref version);
        OnChange?.Invoke();
        _ = BlendeSpaeterAusAsync(meine, art == ToastArt.Fehler ? DauerFehler : DauerNormal);
    }

    private async Task BlendeSpaeterAusAsync(int meine, TimeSpan dauer)
    {
        await Task.Delay(dauer);
        if (meine == Volatile.Read(ref version) && Sichtbar) Verbergen();
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

/// <summary>Globaler Ladezustand (dünner Balken oben) für längere Aktionen.</summary>
public sealed class LadeZustand
{
    private int zaehler;

    public event Action? OnChange;

    public bool Laedt => Volatile.Read(ref zaehler) > 0;

    /// <summary>Startet eine Ladephase; Dispose beendet sie.</summary>
    public IDisposable Beginne()
    {
        Interlocked.Increment(ref zaehler);
        OnChange?.Invoke();
        return new Ende(this);
    }

    private sealed class Ende(LadeZustand zustand) : IDisposable
    {
        private int erledigt;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref erledigt, 1) == 1) return;
            Interlocked.Decrement(ref zustand.zaehler);
            zustand.OnChange?.Invoke();
        }
    }
}
