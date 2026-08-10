using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Mô hình khớp lệnh limit trong kiểm thử: phải xuyên qua mức, hay chỉ cần chạm.
    /// </summary>
    /// <remarks>
    /// ⚠️ Mặc định phải là <c>true</c> (THẬN TRỌNG), không phải giá trị mặc định của kiểu bool.
    /// EF sinh ra <c>defaultValue: false</c> vì đó là CLR default — nhưng <c>false</c> ở đây
    /// nghĩa là mô hình LẠC QUAN, và mọi hàng cấu hình cũ sẽ lặng lẽ chuyển sang giả định làm
    /// đẹp kết quả mà không ai bấm nút nào.
    ///
    /// Quy tắc chung rút ra từ lần trước: giá trị mặc định do EF sinh ra cho cột mới KHÔNG bao
    /// giờ được tin — phải đối chiếu với mặc định của thực thể.
    /// </remarks>
    public partial class AddLimitFillModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BacktestLimitFillRequiresThrough",
                table: "EngineSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BacktestLimitFillRequiresThrough",
                table: "EngineSettings");
        }
    }
}
