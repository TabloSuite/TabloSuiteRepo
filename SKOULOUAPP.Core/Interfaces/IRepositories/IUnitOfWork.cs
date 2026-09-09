using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.Interfaces.IRepositories;

namespace HalloApp.Core.Interfaces.IRepositories;

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
