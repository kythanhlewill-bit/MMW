using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ba cột chi phí quy ra R cho <c>BacktestRuns</c>: phí giao dịch, phí vốn, trượt giá.
    /// </summary>
    /// <remarks>
    /// ⚠️ Các lần chạy CŨ nhận giá trị 0, và 0 ở đây nghĩa là "CHƯA ĐO", không phải "không tốn
    /// chi phí". Trước migration này backtest không hề trừ phí vốn — đọc một lần chạy cũ rồi kết
    /// luận nó không mất phí vốn là đọc ngược hoàn toàn. Muốn so sánh có nghĩa thì phải chạy lại.
    ///
    /// Hai cột <c>TotalFees</c>/<c>TotalSlippage</c> được giữ nguyên nghĩa cũ (% khối lượng và
    /// đơn vị giá) thay vì đổi sang R, vì đổi nghĩa một cột đã có dữ liệu sẽ làm mọi lần chạy cũ
    /// im lặng nói dối.
    /// </remarks>
    public partial class AddBacktestCostInR : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalFeeR",
                table: "BacktestRuns",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalFundingR",
                table: "BacktestRuns",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalSlippageR",
                table: "BacktestRuns",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalFeeR",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "TotalFundingR",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "TotalSlippageR",
                table: "BacktestRuns");
        }
    }
}
