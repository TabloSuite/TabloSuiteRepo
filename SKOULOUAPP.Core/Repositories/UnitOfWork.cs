using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Interfaces.IRepositories;

namespace HalloApp.Core.Repositories;

/// <summary>
/// Arbeitseinheit, die alle Repositories über einen gemeinsamen Datenbankkontext bereitstellt.
/// </summary>
public class UnitOfWork(IHalloAppDbContext dbContext, ICourseRepository courseRepository, ISchoolRepository schoolRepository) : IUnitOfWork
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
