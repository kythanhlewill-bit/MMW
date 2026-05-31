using MMW.Domain.DbContext;
using MMW.Shared.Interfaces;

namespace MMW.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly MmwDbContext _dbContext;

    public UnitOfWork(MmwDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public void Dispose() => _dbContext.Dispose();
}
