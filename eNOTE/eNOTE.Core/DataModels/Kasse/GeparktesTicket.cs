using ENote.Core.DataModels.Katalog;

namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Ticket « en attente »: zwischengespeichertes, noch nicht kassiertes Ticket eines Ladens.
/// Für alle Kassierer des Ladens sichtbar (auch nach Schichtwechsel und Kassenabschluss) und später fortsetzbar –
/// z. B. Kunde geht kurz hinaus, Patient bleibt mehrere Tage in der Praxis. Nichts ist gebucht, kein Lagerabgang.
/// Je Kunde höchstens ein geparktes Ticket (weitere Leistungen werden ergänzt).
/// </summary>
public class GeparktesTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }

    public Guid? KundeId { get; set; }
    public Kunde? Kunde { get; set; }

    /// <summary>Kurze Notiz, z. B. „revient dans 10 min“, „Cabine 2“.</summary>
    public string? Notiz { get; set; }

    /// <summary>Gewählte Remise (wird beim Fortsetzen erneut geprüft).</summary>
    public Guid? RabattId { get; set; }
    public bool RabattBedingungBestaetigt { get; set; }

    public DateTime ErstelltUtc { get; set; }
    public Guid? ErstelltVonId { get; set; }
    public string ErstelltVonName { get; set; } = string.Empty;
    public DateTime GeaendertUtc { get; set; }
    public string GeaendertVonName { get; set; } = string.Empty;

    public List<GeparktesTicketPosition> Positionen { get; set; } = [];
}

public class GeparktesTicketPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid LeistungId { get; set; }
    public Leistung Leistung { get; set; } = null!;
    public int Menge { get; set; }

    /// <summary>Freier Betrag beim Standard-Artikel „Divers“.</summary>
    public decimal? FreierBetrag { get; set; }

    public int Reihenfolge { get; set; }
}
