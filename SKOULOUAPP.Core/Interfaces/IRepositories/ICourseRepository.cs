using HalloApp.Core.DataModels.Courses;

namespace HalloApp.Core.Interfaces.IRepositories;

/// <summary>
/// Repository für Kursniveaus inkl. Katalogabfrage.
/// </summary>
public interface ICourseRepository : IGenericRepository<Klasse>
{
    Task<IReadOnlyList<Klasse>> GetCatalogAsync(CancellationToken cancellationToken = default);
}
