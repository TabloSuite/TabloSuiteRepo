using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Skoulou.WebApp.Services.Plattform;

/// <summary>
/// Ist der Browser dieses Blazor-Kreislaufs gerade verbunden? Klicktest 22.11.2026 (wie eNOTE 9b4f4c4): Endete die Sitzung
/// (Anmeldung desselben Kontos auf einem anderen Gerät), während der Tab schon geschlossen war, leitete
/// <c>ZugriffUmleitung</c> trotzdem zur Anmeldung um – « Navigation failed » und « Unhandled exception in circuit » im Log.
/// Ohne Verbindung wird die Umleitung nachgeholt, sobald der Browser wieder verbunden ist (oder entfällt).
/// </summary>
public sealed class VerbindungsStatus : CircuitHandler
{
    public bool Verbunden { get; private set; } = true;

    /// <summary>Wird nach einer Wiederverbindung ausgelöst.</summary>
    public event Action? WiederVerbunden;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Verbunden = true;
        WiederVerbunden?.Invoke();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Verbunden = false;
        return Task.CompletedTask;
    }
}
