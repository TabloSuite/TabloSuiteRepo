using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Core.DataModels.Finanzen;

/// <summary>
/// Eine einzelne Finanzbuchung (Einnahme oder Ausgabe) einer Schule.
///
/// Audit-Felder werden im Hintergrund gesetzt:
/// - <see cref="AuditableModel.ErstelltAmUtc"/> = Zeitpunkt der Eintragung.
/// - <see cref="ErfasstVonUserID"/> = eingeloggter Nutzer, der die Buchung erfasst hat.
///
/// Das <see cref="Wirkungsdatum"/> ist frei (auch rückwirkend) wählbar und bestimmt,
/// in welchen Zeitraum die Buchung finanziell fällt.
/// </summary>
public class FinanzBuchung : AuditableModel
{
    /// <summary>Schule, zu der die Buchung gehört.</summary>
    public Guid SchulID { get; set; }
    public School Schule { get; set; } = null!;

    /// <summary>Kategorie (Typ) der Buchung.</summary>
    public Guid KategorieID { get; set; }
    public FinanzKategorie Kategorie { get; set; } = null!;

    /// <summary>
    /// Eingefrorene Richtung zum Zeitpunkt der Buchung (redundant zur Kategorie),
    /// damit spätere Kategorieänderungen bestehende Auswertungen nicht verfälschen.
    /// </summary>
    public FinanzRichtung Richtung { get; set; }

    /// <summary>Betrag in FCFA (immer positiv; die Richtung bestimmt das Vorzeichen).</summary>
    public decimal Betrag { get; set; }

    /// <summary>Fachliches Datum der Einnahme/Ausgabe (rückwirkend erlaubt).</summary>
    public DateTime Wirkungsdatum { get; set; }

    /// <summary>Optionale Notiz als Rich-Text-HTML (RTF-Editor).</summary>
    public string? BeschreibungHtml { get; set; }

    /// <summary>Im Hintergrund erfasster Nutzer, der die Buchung angelegt hat.</summary>
    public Guid ErfasstVonUserID { get; set; }
    public AppUser? ErfasstVon { get; set; }

    /// <summary>
    /// Optionale Herkunftsreferenz für automatisch erzeugte Buchungen
    /// (z. B. Id einer Ratenzahlung oder eines LevelEnrollments).
    /// </summary>
    public Guid? HerkunftReferenzID { get; set; }
}
