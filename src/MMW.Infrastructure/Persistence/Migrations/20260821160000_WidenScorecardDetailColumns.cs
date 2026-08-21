using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMW.Domain.DbContext;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations;

/// <summary>Bỏ trần 300 ký tự của hai cột giải thích trên phiếu chấm điểm.</summary>
/// <remarks>
/// Trần cũ làm SQL Server ném "String or binary data would be truncated" và cả phiếu KHÔNG được
/// lưu — 27 lượt chấm điểm biến mất trong ba ngày. Mất phiếu là mất đúng thứ mà bảng này tồn tại
/// để giữ, nên nới cột là lựa chọn đúng thay vì cắt chuỗi ở lớp ứng dụng: cắt thì phiếu còn,
/// nhưng lời giải thích cụt ở giữa câu, và nó cụt đúng ở những phiếu phức tạp nhất.
///
/// ⚠️ Viết TAY, không có tệp <c>.Designer.cs</c>, cùng lý do như
/// <c>20260817150000_AddMinStopDistancePercent</c>: tệp user-secrets của máy phát triển sai cú
/// pháp JSON nên <c>dotnet ef</c> không dựng được host ở design-time.
/// <see cref="MmwDbContextModelSnapshot"/> đã cập nhật thủ công kèm theo.
/// </remarks>
[DbContext(typeof(MmwDbContext))]
[Migration("20260821160000_WidenScorecardDetailColumns")]
public partial class WidenScorecardDetailColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "VetoDetail",
            table: "EntryScorecards",
            type: "nvarchar(max)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(300)",
            oldMaxLength: 300,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "TriggerDetail",
            table: "EntryScorecards",
            type: "nvarchar(max)",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(300)",
            oldMaxLength: 300,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Thu lại sẽ CẮT dữ liệu đang có. Cắt trước bằng LEFT để lệnh ALTER không ném lỗi giữa
        // chừng và bỏ lại cơ sở dữ liệu ở trạng thái nửa vời.
        migrationBuilder.Sql(
            "UPDATE EntryScorecards SET VetoDetail = LEFT(VetoDetail, 300) WHERE LEN(VetoDetail) > 300;");
        migrationBuilder.Sql(
            "UPDATE EntryScorecards SET TriggerDetail = LEFT(TriggerDetail, 300) WHERE LEN(TriggerDetail) > 300;");

        migrationBuilder.AlterColumn<string>(
            name: "VetoDetail",
            table: "EntryScorecards",
            type: "nvarchar(300)",
            maxLength: 300,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "TriggerDetail",
            table: "EntryScorecards",
            type: "nvarchar(300)",
            maxLength: 300,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true);
    }
}
