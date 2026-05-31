using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.DbContext;

/// <summary>
/// Phần partial để mở rộng cấu hình/hành vi của DbContext mà không đụng tới phần sinh tự động.
/// EOffice dùng partial này cho audit history + global query filter; MMW để trống làm chỗ mở rộng.
/// </summary>
public partial class MmwDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // TODO: thêm fluent config / query filter ở đây khi cần.
    }
}
