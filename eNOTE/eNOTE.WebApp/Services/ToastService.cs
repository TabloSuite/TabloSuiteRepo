namespace ENote.WebApp.Services;

/// <summary>Zentraler Toast-Benachrichtigungsdienst (Erfolg/Fehler/Info).</summary>
public class ToastService
{
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
        OnChange?.Invoke();
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
