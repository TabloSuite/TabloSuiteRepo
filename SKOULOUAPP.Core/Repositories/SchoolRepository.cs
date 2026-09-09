using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Repositories;

/// <summary>
/// Repository für Schulen inkl. Lehrer- und Schülerbeziehungen.
/// </summary>
public class SchoolRepository(IHalloAppDbContext dbContext) : GenericRepository<School>(dbContext), ISchoolRepository
{
    public async Task<IReadOnlyList<School>> GetSchoolsWithTeachersAsync(CancellationToken cancellationToken = default)
        => await DbContext.Schools
            .AsNoTracking()
            .Include(x => x.UserSchools)
                .ThenInclude(x => x.AppUser)
            .Include(x => x.Students)
                .ThenInclude(x => x.AppUser)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<School>> GetSchoolsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await DbContext.Schools
            .AsNoTracking()
            .Include(x => x.UserSchools)
            .Include(x => x.Students)
                .ThenInclude(x => x.AppUser)
            .Where(x => x.UserSchools.Any(link => link.AppUserId == userId))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
}
