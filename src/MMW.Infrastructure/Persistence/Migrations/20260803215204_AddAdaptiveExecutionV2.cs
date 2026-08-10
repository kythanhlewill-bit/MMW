using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Tham số của Adaptive scoring &amp; execution V2.
    /// </summary>
    /// <remarks>
    /// <b>Mọi <c>defaultValue</c> dưới đây được ĐIỀN TAY, không dùng giá trị EF tự sinh.</b>
    /// EF mặc định 0 cho mọi cột không nullable, và ở đây 0 không phải "chưa đặt" mà là một cấu
    /// hình HỎNG nhưng vẫn chạy được: <c>MaxConcurrentPositions = 0</c> chặn mọi lệnh,
    /// <c>StopAtrMultipleMax = 0</c> làm mọi dừng lỗ vượt trần, <c>MinDataCoveragePercent = 0</c>
    /// gỡ luôn rào "quá mù để giao dịch". Hàng <c>EngineSettings</c> đang tồn tại sẽ nhận đúng
    /// những giá trị đó và hệ thống chạy sai trong im lặng — không có ngoại lệ nào để lần theo.
    ///
    /// Ba cột của <c>EntryScorecards</c> đi theo hướng ngược lại, cũng có chủ ý:
    /// <c>TotalMaxPoints = 0</c> là giá trị KHÔNG THỂ có ở một lượt chấm thật, nên nó đánh dấu rõ
    /// "phiếu lập trước V2, thang điểm chưa được ghi" thay vì giả vờ 85/85 và làm hỏng mọi thống
    /// kê đối chiếu về sau.
    /// </remarks>
    public partial class AddAdaptiveExecutionV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── EntryScorecards ─────────────────────────────────────────────

            migrationBuilder.AddColumn<int>(
                name: "AvailableMaxPoints",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalMaxPoints",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Phiếu cũ không có hệ số này, và "không có" nghĩa là không bị co — 1.0 mới là sự thật.
            migrationBuilder.AddColumn<decimal>(
                name: "DataMultiplier",
                table: "EntryScorecards",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.0m);

            // ── EngineSettings: đúng bằng mặc định của thực thể ─────────────

            migrationBuilder.AddColumn<decimal>(
                name: "BacktestMakerFeePercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.02m);

            migrationBuilder.AddColumn<int>(
                name: "DirectionMarginPoints",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "LimitEntryExpiryBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<int>(
                name: "MaxConcurrentPositions",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxCorrelatedR",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinCandleBodyRatio",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinDataCoveragePercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 75.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinStructuralRr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.6m);

            migrationBuilder.AddColumn<int>(
                name: "PatternMaxAgeBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<decimal>(
                name: "RangeEdgePercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 25.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StopAtrMultipleMin",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StopAtrMultipleMax",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 3.0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StopStructureBufferAtr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.30m);

            migrationBuilder.AddColumn<int>(
                name: "TimeStopBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 16);

            migrationBuilder.AddColumn<decimal>(
                name: "TimeStopMinR",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AvailableMaxPoints", table: "EntryScorecards");
            migrationBuilder.DropColumn(name: "TotalMaxPoints", table: "EntryScorecards");
            migrationBuilder.DropColumn(name: "DataMultiplier", table: "EntryScorecards");

            migrationBuilder.DropColumn(name: "BacktestMakerFeePercent", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "DirectionMarginPoints", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "LimitEntryExpiryBars", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "MaxConcurrentPositions", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "MaxCorrelatedR", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "MinCandleBodyRatio", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "MinDataCoveragePercent", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "MinStructuralRr", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "PatternMaxAgeBars", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "RangeEdgePercent", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "StopAtrMultipleMin", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "StopAtrMultipleMax", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "StopStructureBufferAtr", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "TimeStopBars", table: "EngineSettings");
            migrationBuilder.DropColumn(name: "TimeStopMinR", table: "EngineSettings");
        }
    }
}
