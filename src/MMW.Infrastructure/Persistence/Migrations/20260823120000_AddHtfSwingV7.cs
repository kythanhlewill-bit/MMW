using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMW.Domain.DbContext;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations;

/// <summary>
/// Nền cho bộ luật swing khung 4 giờ: nhóm lệnh, chốt hai phần, kéo dừng lỗ, và tham số V7.
/// </summary>
/// <remarks>
/// <para>Ba nhóm cột, ba mục đích khác nhau:</para>
///
/// <list type="bullet">
/// <item><b><c>Style</c></b> trên cả lệnh lẫn phiếu — để sổ và báo cáo tách được lệnh ngắn khỏi
/// lệnh swing. Hai nhóm có tỉ lệ thắng và bội R khác hẳn nhau; trộn chung thì con số trung bình
/// không mô tả nhóm nào cả.</item>
/// <item><b>Nhóm chốt hai phần</b> — <c>FirstTakeProfit*</c>, <c>FirstTargetFilledAt</c>,
/// <c>InitialStopLoss</c>, <c>Trail*</c>. Trước đây đường chạy thật đặt đúng một lệnh chốt lời
/// cỡ đầy đủ, nên mọi lệnh không tới đích đều mất trọn 1R kể cả khi đã đi đúng hướng quá nửa
/// đường.</item>
/// <item><b>Tham số <c>V7*</c></b> trên cấu hình engine — khẩu vị của bộ luật mới, chỉnh được
/// mà không phải build lại.</item>
/// </list>
///
/// <para>Mọi cột đều có giá trị mặc định hoặc cho phép rỗng, nên dữ liệu cũ giữ nguyên nghĩa:
/// lệnh cũ là <c>Style = 1</c> (lệnh ngắn), không có mục tiêu gần, không kéo dừng lỗ — đúng
/// hành vi mà chúng đã thật sự chạy.</para>
///
/// ⚠️ Viết TAY, không có tệp <c>.Designer.cs</c>, cùng lý do như
/// <c>20260821160000_WidenScorecardDetailColumns</c>: tệp user-secrets của máy phát triển sai
/// cú pháp JSON nên <c>dotnet ef</c> không dựng được host ở design-time.
/// <see cref="MmwDbContextModelSnapshot"/> đã cập nhật thủ công kèm theo.
/// </remarks>
[DbContext(typeof(MmwDbContext))]
[Migration("20260823120000_AddHtfSwingV7")]
public partial class AddHtfSwingV7 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Nhóm lệnh ──
        migrationBuilder.AddColumn<int>(
            name: "Style", table: "Trades", type: "int", nullable: false, defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "Style", table: "EntryScorecards", type: "int", nullable: false, defaultValue: 1);

        // ── Chốt hai phần và kéo dừng lỗ ──
        migrationBuilder.AddColumn<decimal>(
            name: "FirstTakeProfit", table: "Trades", type: "decimal(18,8)", precision: 18, scale: 8, nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FirstTakeProfitFraction", table: "Trades", type: "decimal(9,4)", precision: 9, scale: 4, nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FirstTakeProfitQuantity", table: "Trades", type: "decimal(18,8)", precision: 18, scale: 8, nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "FirstTargetFilledAt", table: "Trades", type: "datetime2", nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "InitialStopLoss", table: "Trades", type: "decimal(18,8)", precision: 18, scale: 8, nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TrailPivotBars", table: "Trades", type: "int", nullable: false, defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "TrailUpdateCount", table: "Trades", type: "int", nullable: false, defaultValue: 0);

        // Lệnh cũ chưa từng được kéo dừng lỗ, nên dừng lỗ hiện tại CHÍNH LÀ dừng lỗ ban đầu.
        // Không lấp thì mọi báo cáo tính R theo rủi ro gốc sẽ rỗng trên toàn bộ lịch sử.
        migrationBuilder.Sql("UPDATE Trades SET InitialStopLoss = StopLoss WHERE StopLoss IS NOT NULL;");

        // ── Tham số V7 ──
        migrationBuilder.AddColumn<int>(
            name: "V7HtfPivotBars", table: "EngineSettings", type: "int", nullable: false, defaultValue: 3);

        migrationBuilder.AddColumn<int>(
            name: "V7HtfStructureLookbackBars", table: "EngineSettings", type: "int", nullable: false, defaultValue: 60);

        migrationBuilder.AddColumn<decimal>(
            name: "V7ZoneHalfWidthAtr", table: "EngineSettings", type: "decimal(9,4)", precision: 9, scale: 4,
            nullable: false, defaultValue: 0.25m);

        migrationBuilder.AddColumn<int>(
            name: "V7MinZoneConfluence", table: "EngineSettings", type: "int", nullable: false, defaultValue: 2);

        migrationBuilder.AddColumn<decimal>(
            name: "V7StopBufferAtr", table: "EngineSettings", type: "decimal(9,4)", precision: 9, scale: 4,
            nullable: false, defaultValue: 0.25m);

        migrationBuilder.AddColumn<decimal>(
            name: "V7FirstTargetFraction", table: "EngineSettings", type: "decimal(9,4)", precision: 9, scale: 4,
            nullable: false, defaultValue: 0.5m);

        migrationBuilder.AddColumn<decimal>(
            name: "V7MinFirstRr", table: "EngineSettings", type: "decimal(9,4)", precision: 9, scale: 4,
            nullable: false, defaultValue: 1.0m);

        migrationBuilder.AddColumn<decimal>(
            name: "V7MinRunnerRr", table: "EngineSettings", type: "decimal(9,4)", precision: 9, scale: 4,
            nullable: false, defaultValue: 2.5m);

        migrationBuilder.AddColumn<int>(
            name: "V7TrailPivotBars", table: "EngineSettings", type: "int", nullable: false, defaultValue: 2);

        migrationBuilder.AddColumn<int>(
            name: "V7MaxSetupAgeBars", table: "EngineSettings", type: "int", nullable: false, defaultValue: 12);

        migrationBuilder.AddColumn<int>(
            name: "V7MaxConcurrentSwingPositions", table: "EngineSettings", type: "int", nullable: false, defaultValue: 2);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Style", table: "Trades");
        migrationBuilder.DropColumn(name: "Style", table: "EntryScorecards");

        migrationBuilder.DropColumn(name: "FirstTakeProfit", table: "Trades");
        migrationBuilder.DropColumn(name: "FirstTakeProfitFraction", table: "Trades");
        migrationBuilder.DropColumn(name: "FirstTakeProfitQuantity", table: "Trades");
        migrationBuilder.DropColumn(name: "FirstTargetFilledAt", table: "Trades");
        migrationBuilder.DropColumn(name: "InitialStopLoss", table: "Trades");
        migrationBuilder.DropColumn(name: "TrailPivotBars", table: "Trades");
        migrationBuilder.DropColumn(name: "TrailUpdateCount", table: "Trades");

        migrationBuilder.DropColumn(name: "V7HtfPivotBars", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7HtfStructureLookbackBars", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7ZoneHalfWidthAtr", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7MinZoneConfluence", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7StopBufferAtr", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7FirstTargetFraction", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7MinFirstRr", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7MinRunnerRr", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7TrailPivotBars", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7MaxSetupAgeBars", table: "EngineSettings");
        migrationBuilder.DropColumn(name: "V7MaxConcurrentSwingPositions", table: "EngineSettings");
    }
}
