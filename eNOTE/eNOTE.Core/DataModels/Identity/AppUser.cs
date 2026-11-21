using Microsoft.AspNetCore.Identity;

namespace ENote.Core.DataModels.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string Vorname { get; set; } = string.Empty;
    public string Nachname { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? TelefonNummer { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }
    public bool IstErsterAdmin { get; set; }
    public bool IstInternerKassierer { get; set; }

    /// <summary>Blob-Name des Profilbilds (aus Blob Storage), null = kein Bild.</summary>
    public string? ProfilBildBlobName { get; set; }

    /// <summary>
    /// Kennung der aktuell erlaubten Browser-Sitzung. Jede neue Anmeldung ersetzt
    /// sie und meldet damit eine vorherige Sitzung auf einem anderen Gerät ab.
    /// </summary>
    public Guid? ActiveSessionId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = ENote.Core.Services.AppUhr.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<Salons.UserSalon> UserSalons { get; set; } = new List<Salons.UserSalon>();
}
