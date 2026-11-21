namespace ENote.Core.DataModels.Infrastruktur;

/// <summary>
/// Plattformweite Schalter der Administration (eine Zeile, Id = 1; fehlt sie, gelten die Standardwerte). Ändern darf nur
/// der Administrateur (SuperAdmin), in Administration › Réglages – ohne neues Deployment (21.11.2026).
/// </summary>
public class PlattformEinstellung
{
    public const int FesteId = 1;

    public int Id { get; set; } = FesteId;

    /// <summary>« Assistant (Poser une question) pour tous les rôles » – sonst nur Administrateur und Responsable.</summary>
    public bool KiFuerAlleRollen { get; set; }

    public DateTime GeaendertUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public Guid? GeaendertVonId { get; set; }
}
