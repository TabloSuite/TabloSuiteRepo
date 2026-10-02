using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace SkoulouApp.Core.Repositories;

/// <summary>
/// Repository für Kursniveaus inkl. vollständigem Katalog (Lektionen, Übungen, Prüfungen).
/// </summary>
public class CourseRepository(ISkoulouAppDbContext dbContext) : GenericRepository<Klasse>(dbContext), ICourseRepository
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
