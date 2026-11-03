using Microsoft.AspNetCore.SignalR;

namespace ENote.WebApp.Services;

/// <summary>
/// SignalR-Hub für Live-Umsatz-Updates. Clients abonnieren eine Salon-Gruppe;
/// bei jedem gebuchten Verkauf wird der aktuelle Tagesumsatz an die Gruppe gepusht.
/// </summary>
public class UmsatzHub : Hub
{
    public async Task AbonniereSalon(string salonId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, GruppenName(salonId));

    public async Task VerlasseSalon(string salonId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, GruppenName(salonId));

    public static string GruppenName(string salonId) => $"salon-{salonId}";
}

/// <summary>Benachrichtigt abonnierte Clients über neue Verkäufe eines Salons.</summary>
public interface IUmsatzBenachrichtigungService
{
    Task VerkaufGemeldetAsync(Guid salonId, decimal tagesUmsatzGesamt, int anzahlVerkaeufeHeute, CancellationToken cancellationToken = default);
}

/// <summary>SignalR-basierte Implementierung der Live-Umsatz-Benachrichtigung.</summary>
public class UmsatzBenachrichtigungService(IHubContext<UmsatzHub> hubContext) : IUmsatzBenachrichtigungService
{
    public async Task VerkaufGemeldetAsync(
        Guid salonId,
        decimal tagesUmsatzGesamt,
        int anzahlVerkaeufeHeute,
        CancellationToken cancellationToken = default)
    {
        await hubContext.Clients
            .Group(UmsatzHub.GruppenName(salonId.ToString()))
            .SendAsync("UmsatzAktualisiert", tagesUmsatzGesamt, anzahlVerkaeufeHeute, cancellationToken);
    }
}
