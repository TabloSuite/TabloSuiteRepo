using Microsoft.AspNetCore.SignalR;

namespace ENote.WebApp.Services;

/// <summary>
/// SignalR-Hub für Live-Umsatz-Updates und das Kunden-Display.
/// Salon-Gruppen für Umsatz; Display-Gruppen pro Kassensitzung für das Live-Ticket.
/// </summary>
public class UmsatzHub : Hub
{
    public async Task AbonniereSalon(string salonId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, GruppenName(salonId));

    public async Task VerlasseSalon(string salonId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, GruppenName(salonId));

    /// <summary>Kunden-Display abonniert die laufende Kassensitzung.</summary>
    public async Task AbonniereDisplay(string sitzungId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, DisplayGruppenName(sitzungId));

    public async Task VerlasseDisplay(string sitzungId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, DisplayGruppenName(sitzungId));

    public static string GruppenName(string salonId) => $"salon-{salonId}";
    public static string DisplayGruppenName(string sitzungId) => $"display-{sitzungId}";

    /// <summary>
    /// Kunden-Display je Kassengerät (17.11.2026): Arbeiten zwei Geräte gleichzeitig in derselben Kasse, zeigt jedes Display nur
    /// das Ticket SEINES Geräts. Schlüssel = fester Geräteschlüssel aus dem Browser (32 Hex-Zeichen, nicht erratbar).
    /// </summary>
    public async Task AbonniereGeraetDisplay(string geraet)
    {
        if (IstGeraetSchluessel(geraet))
            await Groups.AddToGroupAsync(Context.ConnectionId, GeraetDisplayGruppenName(geraet));
    }

    public async Task VerlasseGeraetDisplay(string geraet)
    {
        if (IstGeraetSchluessel(geraet))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GeraetDisplayGruppenName(geraet));
    }

    public static string GeraetDisplayGruppenName(string geraet) => $"display-geraet-{geraet.ToLowerInvariant()}";

    /// <summary>Gültiger Geräteschlüssel: genau 32 Hex-Zeichen (Guid « N »).</summary>
    public static bool IstGeraetSchluessel(string? geraet)
        => geraet is { Length: 32 } && geraet.All(Uri.IsHexDigit);
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

/// <summary>Eine Zeile des Live-Tickets für das Kunden-Display.</summary>
public record DisplayPosition(string Name, int Menge, decimal ZeilenSumme);

/// <summary>Aktueller Ticket-Stand für das Kunden-Display.</summary>
public record DisplayTicket(
    IReadOnlyList<DisplayPosition> Positionen,
    decimal Zwischensumme,
    decimal RabattSumme,
    decimal GesamtSumme,
    bool Abgeschlossen,
    decimal? Rueckgeld);

/// <summary>Sendet den laufenden Ticket-Stand an das Kunden-Display einer Sitzung.</summary>
public interface IDisplayBenachrichtigungService
{
    Task TicketAktualisiertAsync(Guid sitzungId, DisplayTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>Ticket an das Display EINES Kassengeräts (je Gerät eigenes Display).</summary>
    Task TicketAnGeraetAsync(string geraet, DisplayTicket ticket, CancellationToken cancellationToken = default);
}

public class DisplayBenachrichtigungService(IHubContext<UmsatzHub> hubContext) : IDisplayBenachrichtigungService
{
    public async Task TicketAktualisiertAsync(Guid sitzungId, DisplayTicket ticket, CancellationToken cancellationToken = default)
    {
        await hubContext.Clients
            .Group(UmsatzHub.DisplayGruppenName(sitzungId.ToString()))
            .SendAsync("DisplayAktualisiert", ticket, cancellationToken);
    }

    public async Task TicketAnGeraetAsync(string geraet, DisplayTicket ticket, CancellationToken cancellationToken = default)
    {
        if (!UmsatzHub.IstGeraetSchluessel(geraet)) return;
        await hubContext.Clients
            .Group(UmsatzHub.GeraetDisplayGruppenName(geraet))
            .SendAsync("DisplayAktualisiert", ticket, cancellationToken);
    }
}
