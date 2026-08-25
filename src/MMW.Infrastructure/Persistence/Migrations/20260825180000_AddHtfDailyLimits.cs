using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMW.Domain.DbContext;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations;

/// <summary>Hạn mức trong ngày RIÊNG cho nhóm lệnh swing khung 4 giờ.</summary>
/// <remarks>
/// Ký quỹ của hai bộ luật đã tách được ngay tại sàn (ví USDT / ví USDC ở chế độ ký quỹ đơn tài
/// sản), nhưng các bộ đếm "trong ngày" thì không tách theo. Dùng chung nghĩa là một lệnh swing
/// thua có thể khoá bộ luật trong ngày tới hết ngày UTC.
///
/// ⚠️ Migration này viết TAY, không có tệp <c>.Designer.cs</c> đi kèm, vì
/// <c>dotnet ef migrations add</c> không chạy được: tệp user-secrets của máy phát triển
/// (<c>MMW.Web.20260601.Gemini/secrets.json</c>) sai cú pháp JSON nên host không dựng được ở
/// design-time. <see cref="MmwDbContextModelSnapshot"/> đã được cập nhật thủ công kèm theo.
///
/// Sau khi sửa tệp secrets, nên tạo lại migration này bằng công cụ để có Designer đầy đủ.
/// Việc thiếu Designer KHÔNG ảnh hưởng lúc chạy — <c>MigrateAsync</c> chỉ thực thi <see cref="Up"/>.
/// </remarks>
[DbContext(typeof(MmwDbContext))]
[Migration("20260825180000_AddHtfDailyLimits")]
public partial class AddHtfDailyLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Mặc định 2, không phải 5 như nhóm trong ngày: nhịp hồi khung 4 giờ mỗi ngày chỉ có
        // vài cơ hội, và hạn mức rộng ở đây chỉ mở đường vào lại cùng một ý tưởng.
        migrationBuilder.AddColumn<int>(
            name: "MaxTradesPerDayHtf",
            table: "RiskSettings",
            type: "int",
            nullable: false,
            defaultValue: 2);

        migrationBuilder.AddColumn<decimal>(
            name: "MaxDailyLossPercentHtf",
            table: "RiskSettings",
            type: "decimal(9,4)",
            precision: 9,
            scale: 4,
            nullable: false,
            defaultValue: 3.0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MaxTradesPerDayHtf",
            table: "RiskSettings");

        migrationBuilder.DropColumn(
            name: "MaxDailyLossPercentHtf",
            table: "RiskSettings");
    }
}
