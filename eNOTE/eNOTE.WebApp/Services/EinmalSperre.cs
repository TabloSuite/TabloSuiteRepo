namespace ENote.WebApp.Services;

/// <summary>
/// Lässt eine Aktion nur einmal gleichzeitig laufen (Fehlersuche 21.11.2026: Enter im Feld « Montant reçu » bzw. im PIN-Feld
/// ruft den Handler direkt auf – ohne die Sperre des <c>AsyncActionButton</c>. Zweimal Enter buchte den Verkauf, den Retrait
/// oder die Erstattung zweimal). Weitere Aufrufe, solange die Aktion läuft, werden still verworfen.
/// </summary>
public sealed class EinmalSperre
{
    private int laeuft;

    /// <summary>Läuft gerade eine Aktion?</summary>
    public bool Laeuft => Volatile.Read(ref laeuft) == 1;

    /// <summary>Führt die Aktion aus, falls keine andere läuft. Liefert <c>false</c>, wenn der Aufruf verworfen wurde.</summary>
    public async Task<bool> FuehreAusAsync(Func<Task> aktion)
    {
        if (Interlocked.Exchange(ref laeuft, 1) == 1)
        {
            return false;
        }

        try
        {
            await aktion();
            return true;
        }
        finally
        {
            Volatile.Write(ref laeuft, 0);
        }
    }
}
