using Microsoft.AspNetCore.SignalR;

namespace ENote.WebApp.Services;

/// <summary>
/// SignalR-Hub für Live-Umsatz-Updates und das Kunden-Display.
/// Salon-Gruppen für Umsatz; Display-Gruppen pro Kassensitzung für das Live-Ticket.
/// </summary>
public class UmsatzHub(Microsoft.EntityFrameworkCore.IDbContextFactory<Data.ApplicationDbContext> dbFactory) : Hub
{
    /// <summary>
    /// Live-Umsatz abonnieren – nur mit dem geheimen Salon-Schlüssel (<see cref="SalonSchluessel"/>), nie mit der SalonId:
    /// Der Hub ist anonym erreichbar (Kunden-Display), und die SalonId steht im Display-Link. Fehlersuche 21.11.2026:
    /// mit « AbonniereSalon(SalonId) » empfing jeder ohne Anmeldung Tagesumsatz und Anzahl Verkäufe eines Ladens.
    /// </summary>
    public async Task AbonniereSalon(string salonSchluessel)
    {
        if (IstSalonSchluessel(salonSchluessel))
            await Groups.AddToGroupAsync(Context.ConnectionId, GruppenName(salonSchluessel));
    }

    public async Task VerlasseSalon(string salonSchluessel)
    {
        if (IstSalonSchluessel(salonSchluessel))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GruppenName(salonSchluessel));
    }

    // Geheimnis nur im Arbeitsspeicher dieses Prozesses: Sender (Kasse) und Empfänger (Finanzseite, serverseitige
    // Hub-Verbindung) laufen im selben Prozess; der Schlüssel erreicht nie den Browser.
    private static readonly byte[] SalonGeheimnis = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    /// <summary>Nicht erratbarer Gruppen-Schlüssel eines Ladens (HMAC der SalonId, 64 Hex-Zeichen).</summary>
    public static string SalonSchluessel(Guid salonId)
        => Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(SalonGeheimnis, salonId.ToByteArray()));

    public static bool IstSalonSchluessel(string? schluessel)
        => schluessel is { Length: 64 } && schluessel.All(Uri.IsHexDigit);

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
    /// Seit 22.11.2026 nur mit gültigem Link (<see cref="KundenDisplayLink"/>): Läuft die Kasse inzwischen auf einem anderen
    /// Gerät, wird nicht abonniert (false) – auch nicht von einem Client, der den Hub direkt aufruft.
    /// </summary>
    public async Task<bool> AbonniereGeraetDisplay(string salonId, string geraet)
    {
        if (!Guid.TryParse(salonId, out var laden) || !await KundenDisplayLink.IstGueltigAsync(dbFactory, laden, geraet, Context.ConnectionAborted))
            return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, GeraetDisplayGruppenName(geraet));
        return true;
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
            .Group(UmsatzHub.GruppenName(UmsatzHub.SalonSchluessel(salonId)))
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
