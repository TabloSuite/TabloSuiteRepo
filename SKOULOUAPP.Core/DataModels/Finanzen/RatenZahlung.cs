using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.DataModels.Finanzen;

/// <summary>
/// Zahlungsart einer KursNiveau-Zahlung eines Schülers.
/// </summary>
public enum ZahlungsArt
{
    /// <summary>Einmalige Barzahlung des vollen Betrags.</summary>
    Cash = 0,

    /// <summary>Teilzahlung (Rate) auf den Niveau-Preis.</summary>
    Rate = 1
}

/// <summary>
/// Eine Zahlung (Cash oder Rate) eines Schülers innerhalb eines
/// <see cref="KursNiveauDurchlauf"/>. Jede Zahlung erzeugt automatisch eine
/// zugehörige Einnahme-<see cref="FinanzBuchung"/> (Kategorie KursNiveau-Zahlung).
/// </summary>
public class RatenZahlung : AuditableModel
{
    /// <summary>Durchlauf, zu dem die Zahlung gehört.</summary>
    public Guid DurchlaufID { get; set; }
    public KursNiveauDurchlauf Durchlauf { get; set; } = null!;

    /// <summary>Zahlender (eingeschriebener) Schüler.</summary>
    public Guid StudentId { get; set; }
    public AppUser Student { get; set; } = null!;

    /// <summary>Gezahlter Betrag in FCFA.</summary>
    public decimal Betrag { get; set; }

    /// <summary>Art der Zahlung (Cash oder Rate).</summary>
    public ZahlungsArt ZahlungsArt { get; set; } = ZahlungsArt.Rate;

    /// <summary>Fachliches Zahlungsdatum (rückwirkend erlaubt, >= Start des Durchlaufs).</summary>
    public DateTime Wirkungsdatum { get; set; }

    /// <summary>Optionale Notiz als Rich-Text-HTML.</summary>
    public string? BeschreibungHtml { get; set; }

    /// <summary>Verknüpfte, automatisch erzeugte Einnahme-Buchung.</summary>
    public Guid? FinanzBuchungID { get; set; }
    public FinanzBuchung? FinanzBuchung { get; set; }

    /// <summary>Im Hintergrund erfasster Nutzer, der die Zahlung gebucht hat.</summary>
    public Guid ErfasstVonUserID { get; set; }
    public AppUser? ErfasstVon { get; set; }
}
