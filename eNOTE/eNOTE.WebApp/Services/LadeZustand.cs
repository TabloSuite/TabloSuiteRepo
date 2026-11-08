namespace ENote.WebApp.Services;

/// <summary>
/// Circuit-weiter Ladezustand für Hauptseiten. Zeigt eine Fortschrittsleiste,
/// während eine Seite Daten lädt. Zählerbasiert, damit parallele Ladevorgänge
/// korrekt aggregiert werden.
/// </summary>
public class LadeZustand
{
    private int _aktiv;

    public event Action? OnChange;

    /// <summary>true, solange mindestens ein Ladevorgang läuft.</summary>
    public bool Laedt => _aktiv > 0;

    /// <summary>Beginnt einen Ladevorgang. Rückgabe per IDisposable beenden.</summary>
    public IDisposable Beginnen()
    {
        Interlocked.Increment(ref _aktiv);
        OnChange?.Invoke();
        return new LadeVorgang(this);
    }

    private void Beenden()
    {
        if (Interlocked.Decrement(ref _aktiv) <= 0)
        {
            _aktiv = 0;
        }
        OnChange?.Invoke();
    }

    private sealed class LadeVorgang(LadeZustand zustand) : IDisposable
    {
        private bool _beendet;
        public void Dispose()
        {
            if (_beendet) return;
            _beendet = true;
            zustand.Beenden();
        }
    }
}
