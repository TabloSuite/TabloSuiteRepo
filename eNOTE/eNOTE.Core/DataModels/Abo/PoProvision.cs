namespace ENote.Core.DataModels.Abo;

public enum PoProvisionStatus
{
    /// <summary>Entstanden, noch nicht an den Responsable ausgezahlt.</summary>
    AVerser = 0,
    /// <summary>Vom Administrateur als ausgezahlt markiert, Bestätigung des Responsable steht aus.</summary>
    Versee = 1,
    /// <summary>Vom Responsable als erhalten bestätigt.</summary>
    Recue = 2
}

/// <summary>
/// Provision eines Responsable (ProduktOwner) für EINE bestätigte Abo-Zahlung eines seiner Läden.
/// Entsteht automatisch, sobald die Zahlung bestätigt wird – mit den Sätzen der Zuordnung als Momentaufnahme.
/// Ablauf: à verser → vom Administrateur als « versée » markiert (Datum, Referenz) → vom Responsable als « reçue » bestätigt.
/// Kein Fremdschlüssel auf Abo/Laden: die Provision bleibt, auch wenn ein Laden später gelöscht wird.
/// </summary>
public class PoProvision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProduktOwnerId { get; set; }
    public Identity.AppUser ProduktOwner { get; set; } = null!;

    public Guid NutzungsAboId { get; set; }
    public Guid SalonId { get; set; }
    public string Laden { get; set; } = "";
    public DateTime ZeitraumVonUtc { get; set; }
    public DateTime ZeitraumBisUtc { get; set; }
    public DateTime ZahlungBestaetigtUtc { get; set; }

    /// <summary>Bezahlter Abo-Betrag (Grundlage der Provision).</summary>
    public decimal AboBetrag { get; set; }
    public decimal ProzentProvision { get; set; }
    /// <summary>Fester Anteil – höchstens einmal je Laden und Monat der Bestätigung.</summary>
    public decimal FestProvision { get; set; }
    /// <summary>Provision in ganzen Franc CFA.</summary>
    public decimal Betrag { get; set; }
    public DateTime ErstelltUtc { get; set; }

    /// <summary>Tag der Auszahlung laut Administrateur (null = noch nicht ausgezahlt).</summary>
    public DateTime? VerseeUtc { get; set; }
    public string? VersementReferenz { get; set; }
    public Guid? VersementVonId { get; set; }
    public DateTime? VersementErfasstUtc { get; set; }

    /// <summary>Bestätigung des Responsable, dass das Geld angekommen ist.</summary>
    public DateTime? ErhaltenUtc { get; set; }

    /// <summary>Letzte Meldung « non reçue » des Responsable (für den Administrateur sichtbar, bis erneut ausgezahlt wird).</summary>
    public string? NichtErhaltenHinweis { get; set; }

    public PoProvisionStatus Status => ErhaltenUtc is not null ? PoProvisionStatus.Recue
        : VerseeUtc is not null ? PoProvisionStatus.Versee : PoProvisionStatus.AVerser;
}
