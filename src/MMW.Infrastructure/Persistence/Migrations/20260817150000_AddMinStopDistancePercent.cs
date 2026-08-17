using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMW.Domain.DbContext;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations;

/// <summary>Sàn khoảng cách dừng lỗ tính theo phần trăm giá.</summary>
/// <remarks>
/// ⚠️ Migration này viết TAY, không có tệp <c>.Designer.cs</c> đi kèm, vì
/// <c>dotnet ef migrations add</c> không chạy được: tệp user-secrets của máy phát triển
/// (<c>MMW.Web.20260601.Gemini/secrets.json</c>) sai cú pháp JSON nên host không dựng được ở
/// design-time. <see cref="MmwDbContextModelSnapshot"/> đã được cập nhật thủ công kèm theo.
///
/// Sau khi sửa tệp secrets, nên tạo lại migration này bằng công cụ để có Designer đầy đủ.
/// Việc thiếu Designer KHÔNG ảnh hưởng lúc chạy — <c>MigrateAsync</c> chỉ thực thi <see cref="Up"/>.
/// </remarks>
[DbContext(typeof(MmwDbContext))]
[Migration("20260817150000_AddMinStopDistancePercent")]
public partial class AddMinStopDistancePercent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "MinStopDistancePercent",
            table: "EngineSettings",
            type: "decimal(9,4)",
            precision: 9,
            scale: 4,
            nullable: false,
            defaultValue: 0.40m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MinStopDistancePercent",
            table: "EngineSettings");
    }
}
