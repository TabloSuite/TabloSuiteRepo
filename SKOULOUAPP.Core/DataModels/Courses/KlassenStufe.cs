using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Core.DataModels.Courses;

/// <summary>
/// Schulgebundene Klassenstufe einer normalen Schule, z. B. CEP1.
/// Mehrere konkrete Klassen wie CEP1A und CEP1B können derselben Stufe angehören.
/// </summary>
public class KlassenStufe : AuditableModel
{
    public Guid SchoolId { get; set; }
    public School School { get; set; } = default!;

    public string Name { get; set; } = string.Empty;

    public ICollection<Klasse> Klassen { get; set; } = new List<Klasse>();
}
