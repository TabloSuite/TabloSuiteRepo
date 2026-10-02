using SkoulouApp.Core.DataModels.Courses;

namespace SkoulouApp.Core.Interfaces.IRepositories;

/// <summary>
/// Repository für Kursniveaus inkl. Katalogabfrage.
/// </summary>
public interface ICourseRepository : IGenericRepository<Klasse>
{
    Task<IReadOnlyList<Klasse>> GetCatalogAsync(CancellationToken cancellationToken = default);
}
