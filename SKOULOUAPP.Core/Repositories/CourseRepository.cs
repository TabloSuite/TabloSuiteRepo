using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace HalloApp.Core.Repositories;

/// <summary>
/// Repository für Kursniveaus inkl. vollständigem Katalog (Lektionen, Übungen, Prüfungen).
/// </summary>
public class CourseRepository(IHalloAppDbContext dbContext) : GenericRepository<Klasse>(dbContext), ICourseRepository
{
    public async Task<IReadOnlyList<Klasse>> GetCatalogAsync(CancellationToken cancellationToken = default)
        => await DbContext.Klassen
            .AsNoTracking()
            .Include(x => x.Lessons.OrderBy(l => l.Order))
                .ThenInclude(x => x.Exercises.OrderBy(e => e.Order))
            .Include(x => x.Exams)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
}
