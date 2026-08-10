using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLossStreakMultiplier : Migration
    {
        /// <inheritdoc />
        /// <summary>Thêm hệ số nhân kích thước khi chạm ngưỡng chuỗi thua.</summary>
        /// <remarks>
        /// <c>defaultValue</c> sửa tay khỏi 0 mà EF sinh ra. Để 0 thì tài khoản đang chạy sẽ
        /// nhân kích thước với 0 ngay khi thua hai lệnh liên tiếp — tức chặn hẳn, trong khi
        /// thiết kế chỉ muốn giảm một nửa.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LossStreakSizeMultiplier",
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
            migrationBuilder.DropColumn(
                name: "LossStreakSizeMultiplier",
                table: "EngineSettings");
        }
    }
}
