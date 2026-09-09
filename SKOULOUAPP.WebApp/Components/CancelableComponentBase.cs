using Microsoft.AspNetCore.Components;

namespace HalloApp.WebApp.Components;

/// <summary>
/// Basisklasse für interaktive Blazor-Server-Seiten, die beim Initialisieren
/// Daten aus der Datenbank laden.
///
/// Beim schnellen Navigationswechsel verwirft Blazor die Komponente (und den
/// scoped <c>DbContext</c>), während eine EF-Abfrage noch läuft. Das führt zu
/// <c>InvalidOperationException: "Die Verbindung ist geschlossen."</c>.
///
/// Diese Basisklasse stellt ein an die Lebensdauer der Komponente gebundenes
/// <see cref="CancellationToken"/> bereit (<see cref="ComponentToken"/>), das
/// beim Verwerfen der Komponente abgebrochen wird. Ladevorgänge sollen dieses
/// Token an die Service-/EF-Aufrufe weitergeben und über
/// <see cref="IstAbbruch"/> abgebrochene Vorgänge still ignorieren.
/// </summary>
public abstract class CancelableComponentBase : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource componentCts = new();

    /// <summary>
    /// Token, das beim Verwerfen der Komponente (z. B. Navigationswechsel)
    /// abgebrochen wird. An DB-/Service-Aufrufe im Ladepfad weitergeben.
    /// </summary>
    protected CancellationToken ComponentToken => componentCts.Token;

    /// <summary>
    /// Prüft, ob eine Ausnahme durch das Verwerfen der Komponente entstanden ist
    /// (Abbruch oder bereits disposte Ressourcen). Solche Ausnahmen sind
    /// unkritisch, da die Ergebnisse nicht mehr benötigt werden.
    /// </summary>
    protected bool IstAbbruch(Exception ex)
        => ex is OperationCanceledException or ObjectDisposedException
           || componentCts.IsCancellationRequested
           || IstGeschlosseneVerbindungBeimAbbruch(ex);

    private bool IstGeschlosseneVerbindungBeimAbbruch(Exception ex)
        => ex is InvalidOperationException
           && componentCts.IsCancellationRequested
           && ex.Message.Contains("connection is closed", StringComparison.OrdinalIgnoreCase);

    public virtual void Dispose()
    {
        if (!componentCts.IsCancellationRequested)
        {
            componentCts.Cancel();
        }

        componentCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
