using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MMW.Shared.Models;

namespace MMW.Shared.Interfaces;

/// <summary>
/// Repository nền tảng (gọn hoá từ IBaseRepository của EOffice — giữ các thao tác chính).
/// </summary>
public interface IBaseRepository<TEntity> where TEntity : class
{
    IQueryable<TEntity> Queryable { get; }
    DbContext Context { get; }

    IQueryable<TEntity> Get(Expression<Func<TEntity, bool>> predicate);
    IQueryable<TEntity> GetAll();
    Task<IEnumerable<TEntity>> GetAllAsync();

    Task<TEntity?> FindAsync(object id);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> filter);
    Task<IList<TEntity>> FindListAsync(Expression<Func<TEntity, bool>> filter);

    Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? filter = null);
    Task<int> CountAsync(Expression<Func<TEntity, bool>> filter);

    Task<PaginatedResult<TEntity>> GetPagedAsync(
        int page,
        int pageSize,
        List<Expression<Func<TEntity, bool>>>? filters = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null);

    // Mutations (lưu ý: dùng kèm IUnitOfWork.CommitAsync để chốt transaction)
    Task AddAsync(TEntity entity);
    Task AddRangeAsync(IEnumerable<TEntity> entities);
    void Update(TEntity entity);
    void UpdateRange(IEnumerable<TEntity> entities);
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    Task RemoveAsync(object id);
}
