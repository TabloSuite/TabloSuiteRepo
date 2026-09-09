using HalloApp.Core.DataModels.BaseModel;
using System.Linq.Expressions;

namespace HalloApp.Core.Interfaces.IRepositories;

/// <summary>
/// Generisches Repository für auditierbare Entitäten.
/// </summary>
public interface IGenericRepository<TEntity> where TEntity : AuditableModel
{
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}
