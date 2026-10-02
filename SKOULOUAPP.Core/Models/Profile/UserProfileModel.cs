using SkoulouApp.Core.DataModels.Identity;

namespace SkoulouApp.Core.Models.Profile;

public class UserProfileModel
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Vorname { get; set; } = string.Empty;
    public string Nachname { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? TelefonNummer { get; set; }
    public Geschlecht Geschlecht { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? ProfilBildAssetId { get; set; }
    public string? ProfilBildUrl { get; set; }
    public string? ElternTelefonNummer { get; set; }
    public string? ElternEmail { get; set; }
    public Guid? CurrentKlasseId { get; set; }
    public string? CurrentKlasseAnzeige { get; set; }
    public List<string> Rollen { get; set; } = [];
    public List<UserProfileSchoolItem> Schulen { get; set; } = [];
    public List<UserProfileFachItem> Faecher { get; set; } = [];
}

public class UserProfileSchoolItem
{
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
}

public class UserProfileFachItem
{
    public Guid FachId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
}
