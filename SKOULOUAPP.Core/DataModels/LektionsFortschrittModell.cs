using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels;

public class LektionsFortschrittModell : AuditableModel
{
    public Guid LektionID { get; set; } // ID der Lektion, für die der Fortschritt gespeichert wird.
    public Guid SchuelerID { get; set; } // ID des Schülers, dem der Fortschritt gehört.
    public int KorrekteUebungenAnzahl { get; set; } // Anzahl der korrekt beantworteten Übungen. Z.B. 3, wenn der Schüler 3 von 5 Übungen korrekt beantwortet hat.
    public int UebungenGesamtAnzahl { get; set; } // Gesamtanzahl der Übungen in der Lektion. Z.B. 5, wenn die Lektion 5 Übungen enthält.
    public bool IstAbgeschlossen { get; set; } // Gibt an, ob die Lektion vollständig abgeschlossen wurde. Z.B. true, wenn der Schüler alle Übungen korrekt beantwortet hat.
    public DateTime? AbgeschlossenAmUtc { get; set; } // Zeitpunkt, zu dem die Lektion abgeschlossen wurde. Z.B. 2024-06-01T12:34:56Z, wenn der Schüler die Lektion am 1. Juni 2024 um 12:34 Uhr UTC abgeschlossen hat.
    public DateTime LetzteAktivitaetAmUtc { get; set; } = DateTime.UtcNow; // Zeitpunkt der letzten Aktivität des Schülers in dieser Lektion. Z.B. 2024-06-01T12:34:56Z, wenn der Schüler zuletzt am 1. Juni 2024 um 12:34 Uhr UTC aktiv war.
}






