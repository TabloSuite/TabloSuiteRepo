using SkoulouApp.Core.DataModels.BaseModel;
using SkoulouApp.Core.DataModels.Schools;

namespace SkoulouApp.Core.DataModels.Finanzen;

/// <summary>
/// Konfigurierbarer Typ (Kategorie) für Finanzbuchungen. Eine Kategorie legt
/// fest, ob es sich um eine Einnahme oder Ausgabe handelt.
///
/// Gültigkeit:
/// - <see cref="SchulID"/> == null: globale Kategorie (vom SuperAdmin verwaltet),
///   für alle Schulen sichtbar.
/// - <see cref="SchulID"/> gesetzt: nur für die eigene Schule sichtbar
///   (vom SchulLeiter angelegt).
///
/// Vordefinierte System-Kategorien besitzen einen <see cref="SystemCode"/> ungleich
/// <see cref="FinanzSystemCode.Keiner"/>, sind nicht löschbar und werden für die
/// automatische Verbuchung verwendet.
/// </summary>
public class FinanzKategorie : AuditableModel
{
    /// <summary>Zugehörige Schule; null = globale Kategorie.</summary>
    public Guid? SchulID { get; set; }
    public School? Schule { get; set; }

    /// <summary>Anzeigename der Kategorie (z. B. "Miete", "Verkauf Lehrmaterial").</summary>
    public string Bezeichnung { get; set; } = string.Empty;

    /// <summary>Legt fest, ob Buchungen dieser Kategorie Einnahmen oder Ausgaben sind.</summary>
    public FinanzRichtung Richtung { get; set; }

    /// <summary>Systemcode für vordefinierte Kategorien; <see cref="FinanzSystemCode.Keiner"/> bei freien Kategorien.</summary>
    public FinanzSystemCode SystemCode { get; set; } = FinanzSystemCode.Keiner;

    /// <summary>True bei vordefinierten Kategorien, die nicht gelöscht werden dürfen.</summary>
    public bool IstSystemTyp { get; set; }

    /// <summary>Deaktivierte Kategorien werden ausgeblendet statt gelöscht.</summary>
    public bool IstAktiv { get; set; } = true;

    public ICollection<FinanzBuchung> Buchungen { get; set; } = new List<FinanzBuchung>();
}
