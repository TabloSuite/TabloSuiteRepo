using System.Linq.Expressions;
using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Exams;
using HalloApp.Core.DataModels.Exercises;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.DataModels.Lessons;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces.IRepositories;

namespace HalloApp.Tests.Infrastructure;

/// <summary>
/// Fake-<see cref="IUnitOfWork"/> für Tests. Liefert vorkonfigurierte Kataloge
/// und Schulen; nicht benötigte Methoden sind nicht implementiert.
/// </summary>
public class FakeUnitOfWork : IUnitOfWork
{
    public FakeUnitOfWork(IReadOnlyList<Klasse> catalog, IReadOnlyList<School> schools)
    {
        Courses = new FakeCourseRepository(catalog);
        Schools = new FakeSchoolRepository(schools);
    }

    public ICourseRepository Courses { get; }
    public ISchoolRepository Schools { get; }
    public IGenericRepository<Permission> Permissions => throw new NotImplementedException();
    public IGenericRepository<Lesson> Lessons => throw new NotImplementedException();
    public IGenericRepository<Exercise> Exercises => throw new NotImplementedException();
    public IGenericRepository<Exam> Exams => throw new NotImplementedException();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    private sealed class FakeCourseRepository(IReadOnlyList<Klasse> catalog) : ICourseRepository
    {
        public Task<IReadOnlyList<Klasse>> GetCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(catalog);

        public Task<IReadOnlyList<Klasse>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(catalog);

        public Task<IReadOnlyList<Klasse>> FindAsync(Expression<Func<Klasse, bool>> predicate, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Klasse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task AddAsync(Klasse entity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public void Update(Klasse entity) => throw new NotImplementedException();
        public void Remove(Klasse entity) => throw new NotImplementedException();
    }

    private sealed class FakeSchoolRepository(IReadOnlyList<School> schools) : ISchoolRepository
    {
        public Task<IReadOnlyList<School>> GetSchoolsWithTeachersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(schools);

        public Task<IReadOnlyList<School>> GetSchoolsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(schools);

        public Task<IReadOnlyList<School>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(schools);

        public Task<IReadOnlyList<School>> FindAsync(Expression<Func<School, bool>> predicate, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<School?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task AddAsync(School entity, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public void Update(School entity) => throw new NotImplementedException();
        public void Remove(School entity) => throw new NotImplementedException();
    }
}
