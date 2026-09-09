using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using Microsoft.AspNetCore.Identity;

namespace HalloApp.Core.DataModels.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string Vorname { get; set; } = string.Empty;
    public string Nachname { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    /// <summary>Optionale Telefonnummer des Benutzers.</summary>
    public string? TelefonNummer { get; set; }
    public Geschlecht Geschlecht { get; set; } = Geschlecht.Divers;
    public bool IsBlocked { get; set; }
    public bool IsSelfRegistered { get; set; }

    /// <summary>
    /// Kennung der aktuell erlaubten Browser-Sitzung. Jede neue Anmeldung ersetzt
    /// sie und meldet damit eine vorherige Sitzung auf einem anderen Gerät ab.
    /// </summary>
    public Guid? ActiveSessionId { get; set; }

    /// <summary>
    /// Optionales Geburtsdatum des Benutzers (UTC).
    /// </summary>
    public DateTime? GeburtsdatumUtc { get; set; }

    public string? AgbVersion { get; set; }
    public DateTime? AgbAcceptedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<AppUserRole> UserRoles { get; set; } = new List<AppUserRole>();
    public ICollection<UserSchool> UserSchools { get; set; } = new List<UserSchool>();
    public ICollection<StudentProfile> StudentProfiles { get; set; } = new List<StudentProfile>();
    public ICollection<LevelEnrollment> LevelEnrollments { get; set; } = new List<LevelEnrollment>();
}







