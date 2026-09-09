using HalloApp.Core.DataModels.Schools;

namespace HalloApp.Core.Interfaces.IRepositories;

/// <summary>
/// Repository für Schulen inkl. Lehrer- und Schülerbeziehungen.
/// </summary>
public interface ISchoolRepository : IGenericRepository<School>
{
    Task<IReadOnlyList<School>> GetSchoolsWithTeachersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<School>> GetSchoolsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
