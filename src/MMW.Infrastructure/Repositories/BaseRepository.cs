using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MMW.Shared.Interfaces;
using MMW.Shared.Models;

namespace MMW.Infrastructure.Repositories;

/// <summary>
/// Cài đặt repository nền tảng (gọn hoá từ BaseRepository của EOffice).
/// Các thao tác ghi KHÔNG tự SaveChanges — chốt qua IUnitOfWork.CommitAsync.
/// </summary>
public class BaseRepository<TEntity> : IBaseRepository<TEntity> where TEntity : class
{
    protected readonly DbContext _dbContext;
    protected readonly DbSet<TEntity> _dbSet;

    public BaseRepository(DbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dbSet = _dbContext.Set<TEntity>();
    }

    public IQueryable<TEntity> Queryable => _dbSet.AsQueryable();
    public DbContext Context => _dbContext;

    public IQueryable<TEntity> Get(Expression<Func<TEntity, bool>> predicate) =>
        _dbSet.AsNoTracking().Where(predicate);

    public IQueryable<TEntity> GetAll() => _dbSet.AsNoTracking();

    public async Task<IEnumerable<TEntity>> GetAllAsync() =>
        await _dbSet.AsNoTracking().ToListAsync();

    public async Task<TEntity?> FindAsync(object id) => await _dbSet.FindAsync(id);

    public async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter) =>
        await _dbSet.AsNoTracking().FirstOrDefaultAsync(filter);

    public async Task<IList<TEntity>> FindListAsync(Expression<Func<TEntity, bool>> filter) =>
        await _dbSet.AsNoTracking().Where(filter).ToListAsync();

    public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? filter = null) =>
        filter == null
            ? await _dbSet.AsNoTracking().AnyAsync()
            : await _dbSet.AsNoTracking().AnyAsync(filter);

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>> filter) =>
        await _dbSet.CountAsync(filter);

    public async Task<PaginatedResult<TEntity>> GetPagedAsync(
        int page,
        int pageSize,
        List<Expression<Func<TEntity, bool>>>? filters = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking();

        if (filters != null)
        {
            foreach (var filter in filters)
                query = query.Where(filter);
        }

        var totalCount = await query.CountAsync();

        if (orderBy != null)
            query = orderBy(query);

        var data = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PaginatedResult<TEntity>(true, data, null, totalCount, page, pageSize);
    }

    public async Task AddAsync(TEntity entity) => await _dbSet.AddAsync(entity);

    public async Task AddRangeAsync(IEnumerable<TEntity> entities) => await _dbSet.AddRangeAsync(entities);

    public void Update(TEntity entity) => _dbSet.Update(entity);

    public void UpdateRange(IEnumerable<TEntity> entities) => _dbSet.UpdateRange(entities);

    public void Remove(TEntity entity) => _dbSet.Remove(entity);

    public void RemoveRange(IEnumerable<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            var entry = _dbContext.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Deleted;
            }
            else
            {
                // Nếu entity là detached (từ AsNoTracking), kiểm tra xem đã có
                // instance cùng key đang được tracked chưa để tránh identity conflict.
                var pk = entry.Metadata.FindPrimaryKey()!;
                var keyValues = pk.Properties
                    .Select(p => entry.Property(p.Name).CurrentValue)
                    .ToArray();
                var trackedEntry = _dbContext.ChangeTracker.Entries<TEntity>()
                    .FirstOrDefault(e =>
                    {
                        var trackedKeys = pk.Properties
                            .Select(p => e.Property(p.Name).CurrentValue)
                            .ToArray();
                        return keyValues.SequenceEqual(trackedKeys);
                    });
                if (trackedEntry != null)
                    trackedEntry.State = EntityState.Deleted;
                else
                    _dbSet.Remove(entity);
            }
        }
    }

    public async Task RemoveAsync(object id)
    {
        var entity = await _dbSet.FindAsync(id);
        if (entity != null)
            _dbSet.Remove(entity);
    }
}
