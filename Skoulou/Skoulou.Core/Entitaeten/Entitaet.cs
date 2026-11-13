namespace Skoulou.Core.Entitaeten;

/// <summary>Zeitstempel in UTC; werden beim Speichern automatisch gesetzt.</summary>
public interface IZeitgestempelt
{
    DateTime ErstelltAmUtc { get; set; }
    DateTime GeaendertAmUtc { get; set; }
}

/// <summary>Entität, die genau einer Schule (Mandant) gehört.</summary>
public interface ISchulgebunden
{
    Guid SchuleId { get; }
}

/// <summary>Basis aller Entitäten: fortlaufender Guid-Schlüssel (schon beim Anlegen gesetzt) und Zeitstempel.</summary>
public abstract class Entitaet : IZeitgestempelt
{
    public Guid Id { get; set; } = NeueId.Erzeuge();
    public DateTime ErstelltAmUtc { get; set; }
    public DateTime GeaendertAmUtc { get; set; }
}
