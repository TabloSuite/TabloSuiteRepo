using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Core.Models.Billing;

/// <summary>
/// Rolle, die eine Provision aus einem bezahlten Abo erhält.
/// </summary>
public enum ProvisionsEmpfaenger
{
    /// <summary>Schulleiter (Direktor) der Schule.</summary>
    Schulleiter = 0,

    /// <summary>Der dem Niveau zugeordnete Lehrer.</summary>
    Lehrer = 1,

    /// <summary>Betreiber/SuperAdmin.</summary>
    Admin = 2,

    /// <summary>ProductOwner-Anteil für zugewiesene Schulen.</summary>
    ProductOwner = 3
}

/// <summary>
/// Ein einzelner, auf eine Rolle entfallender Provisionsanteil einer bezahlten
/// Niveau-Freischaltung.
/// </summary>
public class ProvisionsAnteil
{
    /// <summary>Freischaltung (LevelEnrollment), aus der die Provision stammt.</summary>
    public Guid FreischaltungID { get; set; }

    /// <summary>Schüler, dessen bezahltes Abo die Provision erzeugt hat.</summary>
    public Guid SchuelerID { get; set; }

    /// <summary>Niveau-Code (z. B. "A1.1") der Freischaltung.</summary>
    public string NiveauCode { get; set; } = string.Empty;

    /// <summary>Abo-Typ der Freischaltung.</summary>
    public AboTyp AboTyp { get; set; }

    /// <summary>Empfänger-Rolle dieses Anteils.</summary>
    public ProvisionsEmpfaenger Empfaenger { get; set; }

    /// <summary>
    /// Optionaler Benutzerbezug für ältere Detailauswertungen. Die aktive Provisionslogik
    /// aggregiert Lehreranteile pro Schule und nutzt keine individuellen Lehrer-Auszahlungen mehr.
    /// </summary>
    public Guid? LehrerID { get; set; }

    /// <summary>Betrag dieses Anteils in FCFA.</summary>
    public decimal Betrag { get; set; }
}
