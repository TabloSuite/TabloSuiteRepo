namespace ENote.WebApp.Services;

/// <summary>
/// Circuit-weiter Kassen-Modus: Nach dem PIN-Start ist nur die Kasse nutzbar.
/// Verhindert, dass ein Kassierer am gemeinsamen Tablet (angemeldetes Besitzer-Konto)
/// in Besitzer-Bereiche (Finanzen, Verwaltung) gelangt. Endet beim Kassenabschluss
/// oder wenn der Besitzer den Modus beendet.
/// </summary>
public class KassenModusState
{
    public event Action? OnChange;

    /// <summary>true, solange eine Kassensitzung läuft (nur Kasse erlaubt).</summary>
    public bool IstAktiv { get; private set; }

    public void Aktivieren()
    {
        IstAktiv = true;
        OnChange?.Invoke();
    }

    public void Deaktivieren()
    {
        IstAktiv = false;
        OnChange?.Invoke();
    }
}
