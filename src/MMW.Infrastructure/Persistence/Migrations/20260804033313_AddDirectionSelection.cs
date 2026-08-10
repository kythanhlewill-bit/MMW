using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Bốn cột ghi lại phép chọn chiều của V2 §4 trên <c>EntryScorecards</c>.
    /// </summary>
    /// <remarks>
    /// <b>Giá trị mặc định do EF sinh ra ĐÃ ĐƯỢC đối chiếu, và lần này chúng đúng.</b> Quy tắc rút
    /// ra từ hai lần trước là không bao giờ TIN chúng, không phải là luôn phải SỬA chúng.
    ///
    /// <list type="bullet">
    /// <item><c>DirectionalScore = 0</c> cho phiếu cũ là giá trị không thể hiểu nhầm: trước §4
    /// không có phép so hai chiều nào, nên 0 nghĩa là "chưa từng đo" — giống hệt cách
    /// <c>TotalMaxPoints = 0</c> đánh dấu phiếu lập trước bước chuẩn hoá điểm.</item>
    /// <item>Ba cột còn lại nullable, và NULL nói đúng điều cần nói: không có chiều đối lập nào
    /// được chấm, không có biên độ nào được đo.</item>
    /// </list>
    ///
    /// Khác hẳn <c>EngineSettings</c>, nơi 0 là một cấu hình HỎNG NHƯNG CHẠY ĐƯỢC và phải điền tay.
    /// </remarks>
    public partial class AddDirectionSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DirectionalScore",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OppositeDirectionalScore",
                table: "EntryScorecards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OppositeScore",
                table: "EntryScorecards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RangePositionPercent",
                table: "EntryScorecards",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DirectionalScore",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "OppositeDirectionalScore",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "OppositeScore",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "RangePositionPercent",
                table: "EntryScorecards");
        }
    }
}
