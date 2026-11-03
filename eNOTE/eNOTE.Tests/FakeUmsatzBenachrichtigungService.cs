using ENote.WebApp.Services;

namespace ENote.Tests;

/// <summary>Test-Double: nimmt Benachrichtigungen entgegen, ohne SignalR zu benötigen.</summary>
internal sealed class FakeUmsatzBenachrichtigungService : IUmsatzBenachrichtigungService
{
    public int Aufrufe { get; private set; }
    public Guid? LetzterSalonId { get; private set; }
    public decimal LetzterTagesUmsatz { get; private set; }

    public Task VerkaufGemeldetAsync(Guid salonId, decimal tagesUmsatzGesamt, int anzahlVerkaeufeHeute, CancellationToken cancellationToken = default)
    {
        Aufrufe++;
        LetzterSalonId = salonId;
        LetzterTagesUmsatz = tagesUmsatzGesamt;
        return Task.CompletedTask;
    }
}
