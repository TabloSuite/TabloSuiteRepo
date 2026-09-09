using HalloApp.Core.DataModels.BaseModel;

namespace HalloApp.Core.DataModels;

public class SchuelerProfilModell : AuditableModel
{
    public Guid BenutzerID { get; set; } // ID des Benutzerkontos, zu dem das Schülerprofil gehört.
    public Guid SchulID { get; set; } // ID der Schule, der der Schüler zugeordnet ist.
    public string Anzeigename { get; set; } = string.Empty; // Anzeigename des Schülers in der Anwendung.
    public string AktuellerKursNiveauCode { get; set; } = string.Empty;  // Aktueller Kursniveau-Code des Schülers.
    public int AbgeschlosseneLektionenAnzahl { get; set; } // Anzahl der bereits abgeschlossenen Lektionen.
}






