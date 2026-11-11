namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Anwesenheit eines Kunden im Laden bzw. in der Praxis: « Client présent pour … », Raum optional.
/// Offen, solange <see cref="EndeUtc"/> leer ist; endet von Hand oder automatisch beim Encaissement.
/// </summary>
public class KundenBesuch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public Guid KundeId { get; set; }
    public Kunde Kunde { get; set; } = null!;

    /// <summary>Anlass, z. B. „Coupe“, „Consultation“, „Pansement“.</summary>
    public string Grund { get; set; } = string.Empty;
    /// <summary>Optionaler Raum/Platz, z. B. „Salle 2“, „Bac“.</summary>
    public string? Raum { get; set; }

    public DateTime AnkunftUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndeUtc { get; set; }

    public Guid? ErfasstVonId { get; set; }
    public string ErfasstVonName { get; set; } = string.Empty;

    /// <summary>Verkauf, mit dem der Besuch beim Encaissement beendet wurde.</summary>
    public Guid? VerkaufId { get; set; }
}
