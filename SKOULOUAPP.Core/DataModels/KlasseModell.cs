using SkoulouApp.Core.DataModels.BaseModel;

namespace SkoulouApp.Core.DataModels;

public class KlasseModell : AuditableModel
{
    public string Code { get; set; } = string.Empty; // Eindeutiger Code der Klasse, zum Beispiel A1 oder B2.
    public string Name { get; set; } = string.Empty; //// Anzeigename der Klasse. Z.B. "Anfänger A1" oder "Fortgeschrittene B2".
    public string Beschreibung { get; set; } = string.Empty; //// Beschreibung der Klasse und ihrer Lernziele. Z.B. "Diese Klasse richtet sich an Anfänger, die grundlegende Sprachkenntnisse erwerben möchten."
    public int Reihenfolge { get; set; } // Sortierreihenfolge der Klasse innerhalb der Kursliste. Z.B. 1 für A1, 2 für A2, 3 für B1 usw.
    public bool IstAktiv { get; set; } = true; // Gibt an, ob die Klasse aktuell verwendet werden kann. Z.B. true, wenn die Klasse aktiv ist, false, wenn sie deaktiviert wurde.
}






