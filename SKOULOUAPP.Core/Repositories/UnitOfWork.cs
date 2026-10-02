using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.Interfaces;
using SkoulouApp.Core.Interfaces.IRepositories;

namespace SkoulouApp.Core.Repositories;

/// <summary>
/// Arbeitseinheit, die alle Repositories über einen gemeinsamen Datenbankkontext bereitstellt.
/// </summary>
public class UnitOfWork(ISkoulouAppDbContext dbContext, ICourseRepository courseRepository, ISchoolRepository schoolRepository) : IUnitOfWork
{
    public ICourseRepository Courses { get; } = courseRepository;
    public ISchoolRepository Schools { get; } = schoolRepository;
    public IGenericRepository<Permission> Permissions { get; } = new GenericRepository<Permission>(dbContext);
    public IGenericRepository<Lesson> Lessons { get; } = new GenericRepository<Lesson>(dbContext);
    public IGenericRepository<Exercise> Exercises { get; } = new GenericRepository<Exercise>(dbContext);
    public IGenericRepository<Exam> Exams { get; } = new GenericRepository<Exam>(dbContext);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => dbContext.SaveChangesAsync(cancellationToken);
}
