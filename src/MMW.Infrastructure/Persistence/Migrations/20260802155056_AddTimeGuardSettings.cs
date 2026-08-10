using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeGuardSettings : Migration
    {
        /// <summary>
        /// Thêm năm ngưỡng cấu hình của tầng chặn theo khung giờ.
        /// </summary>
        /// <remarks>
        /// Các <c>defaultValue</c> dưới đây được SỬA TAY khỏi giá trị 0 mà EF sinh ra, và phải
        /// khớp với giá trị khởi tạo trong <c>EngineSetting</c>.
        ///
        /// Lý do: giá trị khởi tạo của thuộc tính C# chỉ áp cho thực thể MỚI, không áp cho các
        /// dòng đã nằm sẵn trong cơ sở dữ liệu. Để nguyên 0 thì tài khoản đang chạy sẽ nhận
        /// <c>BlackoutLeadMinutes = 0</c> — nghĩa là không bao giờ nhìn thấy cửa sổ chặn sắp
        /// tới, nên KHÔNG BAO GIỜ làm phẳng vị thế trước tin. Lớp bảo vệ biến mất mà không có
        /// lỗi nào, đúng kiểu hỏng mà cả tầng này sinh ra để tránh.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BlackoutBreakevenAtR",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.5m);

            migrationBuilder.AddColumn<int>(
                name: "BlackoutLeadMinutes",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<decimal>(
                name: "BlackoutPartialClosePercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 50m);

            migrationBuilder.AddColumn<int>(
                name: "ClockDriftToleranceSeconds",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "SessionStatsSmoothingTrades",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlackoutBreakevenAtR",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "BlackoutLeadMinutes",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "BlackoutPartialClosePercent",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "ClockDriftToleranceSeconds",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "SessionStatsSmoothingTrades",
                table: "EngineSettings");
        }
    }
}
