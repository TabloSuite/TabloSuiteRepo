namespace ENote.Core.DataModels.Kasse;

/// <summary>
/// Raum bzw. Platz eines Ladens oder Cabinets (« Salle 1 », « Bac », « Poste 2 »), vom Besitzer in den Paramètres festgelegt.
/// Erscheint als Schnellauswahl « Salle / poste » bei « Nouvelle arrivée ». Ohne festgelegte Räume schlägt eNOTE die
/// zuletzt benutzten vor. Besuche speichern den Namen als Text – Umbenennen ändert alte Besuche nicht.
/// </summary>
public class SalonRaum
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalonId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Reihenfolge { get; set; }
    public DateTime ErstelltUtc { get; set; }
}
