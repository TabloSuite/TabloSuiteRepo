using SkoulouApp.Core.DataModels.Exams;
using SkoulouApp.Core.DataModels.Exercises;
using SkoulouApp.Core.DataModels.Identity;
using SkoulouApp.Core.DataModels.Lessons;
using SkoulouApp.Core.Interfaces.IRepositories;

namespace SkoulouApp.Core.Interfaces.IRepositories;

/// <summary>
/// Bündelt die Repositories und die Speicherung als eine Arbeitseinheit.
/// </summary>
public interface IUnitOfWork
{
    ICourseRepository Courses { get; }
    ISchoolRepository Schools { get; }
    IGenericRepository<Permission> Permissions { get; }
    IGenericRepository<Lesson> Lessons { get; }
    IGenericRepository<Exercise> Exercises { get; }
    IGenericRepository<Exam> Exams { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
