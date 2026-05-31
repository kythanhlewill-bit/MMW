using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MMW.Domain.DbContext;

namespace MMW.Infrastructure.Persistence;

/// <summary>
/// Factory dùng cho design-time (dotnet ef migrations / database update).
/// Cho phép chạy lệnh EF mà không cần khởi động cả MMW.Web.
/// </summary>
public class MmwDbContextFactory : IDesignTimeDbContextFactory<MmwDbContext>
{
    public MmwDbContext CreateDbContext(string[] args)
    {
        // Connection string mặc định cho local dev — chỉnh theo máy bạn khi cần.
        var connectionString = Environment.GetEnvironmentVariable("MMW_CONNECTION")
            ?? "Server=localhost;Database=MMW;Trusted_Connection=True;TrustServerCertificate=True;";

        var options = new DbContextOptionsBuilder<MmwDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(MmwDbContextFactory).Assembly.FullName))
            .Options;

        return new MmwDbContext(options);
    }
}
