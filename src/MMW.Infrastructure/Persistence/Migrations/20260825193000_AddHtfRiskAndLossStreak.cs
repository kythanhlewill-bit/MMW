using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMW.Domain.DbContext;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations;

/// <summary>Cỡ lệnh và ngưỡng chuỗi thua RIÊNG cho nhóm swing khung 4 giờ.</summary>
/// <remarks>
/// Hai hạn mức cuối cùng còn dùng chung giữa hai bộ luật chạy song song.
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
[Migration("20260825193000_AddHtfRiskAndLossStreak")]
public partial class AddHtfRiskAndLossStreak : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Mặc định 1% và cố ý KHÔNG sao chép giá trị đang có của nhóm lệnh ngắn. Bộ luật swing
        // có thể chưa từng chạy thật lần nào; thừa hưởng cỡ lệnh mà nhóm ngắn phải chạy hàng
        // chục lệnh mới dám dùng nghĩa là lệnh swing ĐẦU TIÊN vào bằng đúng cỡ đó.
        migrationBuilder.AddColumn<decimal>(
            name: "MaxRiskPerTradePercentHtf",
            table: "RiskSettings",
            type: "decimal(9,4)",
            precision: 9,
            scale: 4,
            nullable: false,
            defaultValue: 1.0m);

        // Mặc định 3 — giá trị mặc định của chính hệ, không phải giá trị đang đặt cho nhóm ngắn.
        migrationBuilder.AddColumn<int>(
            name: "LossStreakThresholdHtf",
            table: "RiskSettings",
            type: "int",
            nullable: false,
            defaultValue: 3);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MaxRiskPerTradePercentHtf",
            table: "RiskSettings");

        migrationBuilder.DropColumn(
            name: "LossStreakThresholdHtf",
            table: "RiskSettings");
    }
}
