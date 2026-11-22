using Microsoft.AspNetCore.Components.Server.Circuits;

namespace ENote.WebApp.Services;

/// <summary>
/// Ist der Browser dieses Blazor-Kreislaufs gerade verbunden? Fehlersuche 21.11.2026: Der SitzungsWaechter leitete ein
/// abgemeldetes Gerät auch dann zur Anmeldung um, wenn der Tab schon geschlossen war – « Navigation failed » und
/// « Unhandled exception in circuit » landeten im Journal. Ohne Verbindung wird die Umleitung jetzt nachgeholt, sobald
/// der Browser wieder verbunden ist (oder entfällt, wenn er nicht zurückkommt).
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
