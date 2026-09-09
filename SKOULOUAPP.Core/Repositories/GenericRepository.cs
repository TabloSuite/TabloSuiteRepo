using HalloApp.Core.DataModels.BaseModel;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Interfaces.IRepositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HalloApp.Core.Repositories;

/// <summary>
/// Generische Repository-Implementierung auf Basis von <see cref="IHalloAppDbContext"/>.
/// </summary>
public class GenericRepository<TEntity>(IHalloAppDbContext dbContext) : IGenericRepository<TEntity>
    where TEntity : AuditableModel
{
    protected readonly IHalloAppDbContext DbContext = dbContext;
    protected readonly DbSet<TEntity> DbSet = dbContext.Set<TEntity>();

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => await DbSet.AsNoTracking().Where(predicate).ToListAsync(cancellationToken);

    public async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await DbSet.FindAsync([id], cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await DbSet.AddAsync(entity, cancellationToken);

    public void Update(TEntity entity)
    {
        entity.GeaendertAmUtc = DateTime.UtcNow;
        DbSet.Update(entity);
    }

    public void Remove(TEntity entity)
        => DbSet.Remove(entity);
}
