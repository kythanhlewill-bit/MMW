using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Sáu tham số điều chỉnh lợi nhuận, rút ra từ 2.900 phiếu có kết cục và 35 lệnh thật đã đóng
    /// của đợt chạy thử 18–28/08.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>defaultValue</c> ở đây PHẢI trùng với giá trị mặc định trong <c>EngineSetting</c>.
    /// EF sinh ra <c>0</c> cho mọi kiểu số và <c>""</c> cho chuỗi, và với bộ tham số này thì con
    /// số 0 không phải "chưa cấu hình" mà là một cấu hình SAI theo hai chiều ngược nhau:
    ///
    /// <code>
    /// MaxExpectedCostR      = 0  ⟹ mọi lệnh đều "vượt trần chi phí" ⟹ engine không vào lệnh nào
    /// MaxHoldingHours*      = 0  ⟹ tắt dừng thời gian ⟹ đúng cái bế tắc migration này sinh ra để gỡ
    /// MinSizeMultiplierProduct = 0 ⟹ tắt kẹp cỡ lệnh
    /// DisabledSetupTypes    = "" ⟹ ba setup thua vẫn chạy
    /// </code>
    ///
    /// Bảng <c>EngineSettings</c> chỉ có vài hàng và chúng đều là hàng ĐANG CHẠY, nên "giá trị
    /// mặc định cho hàng cũ" ở đây chính là cấu hình sản xuất. Viết đúng nó ngay trong migration
    /// là cách duy nhất để việc triển khai không cần một bước UPDATE thủ công mà ai đó sẽ quên.
    /// </remarks>
    public partial class AddProfitOptimisationGates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisabledSetupTypes",
                table: "EngineSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "RectangleRangeFade,RectangleBreakout,TriangleBreakout");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxExpectedCostR",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.25m);

            migrationBuilder.AddColumn<int>(
                name: "MaxHoldingHoursIntraday",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "MaxHoldingHoursSwing",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.AddColumn<decimal>(
                name: "MinSizeMultiplierProduct",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.5m);

            // 0 = TẮT. Cột này tồn tại nhưng không được bật: đo trên 64 phiếu vào lệnh có kết
            // cục, chiều bán khống TỐT hơn chiều mua (+0,498 so với −0,108 net R). Khoản lỗ của
            // Short trên lệnh thật là méo do cỡ lệnh, không phải do chiều. Xem
            // EngineSetting.ShortEntryScorePenalty.
            migrationBuilder.AddColumn<int>(
                name: "ShortEntryScorePenalty",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // ── Bảng chất lượng phiên: đảo lại theo kết cục đo được ─────────────
            //
            // Bảng đang chạy là bảng phiên CHUẨN của thị trường (London/NY là giờ vàng). Trên sổ
            // này nó ngược gần như hoàn toàn — net R sau phí, 2.900 phiếu, cùng khung giờ mà bảng
            // đang chấm điểm:
            //
            //   21–24 Đêm mỏng      −0,162  ⟵ TỐT NHẤT, đang bị chấm 1 (thấp nhất)
            //   07–09 Mở London     −0,185
            //   16–21 NY chiều      −0,210
            //   00–07 Phiên Á       −0,236       đang bị chấm 2
            //   13–16 Chồng lấn NY  −0,444       đang được chấm 6 (cao nhất)
            //   09–13 London        −0,492  ⟵ TỆ NHẤT, đang được chấm 5
            //
            // SessionQualityProvider trộn điểm bảng với tỷ lệ thắng thật theo cỡ mẫu, nên bảng
            // sai không bị nuốt mất — nó kéo ngược điểm cá nhân về phía sai suốt thời gian mẫu
            // còn nhỏ. Sửa bảng là sửa cái mỏ neo đó.
            //
            // Điểm mới bám thứ hạng đo được, không phải bám tên phiên. Giữ nguyên thang 0–6 và
            // giữ nguyên ranh giới giờ để không đụng vào ràng buộc phủ kín 0–24 của bảng.
            migrationBuilder.Sql("""
                UPDATE SessionQualityRows SET Score = 5 WHERE FromHourUtc = 21 AND ToHourUtc = 24;
                UPDATE SessionQualityRows SET Score = 4 WHERE FromHourUtc = 0  AND ToHourUtc = 7;
                UPDATE SessionQualityRows SET Score = 2 WHERE FromHourUtc = 13 AND ToHourUtc = 16;
                UPDATE SessionQualityRows SET Score = 1 WHERE FromHourUtc = 9  AND ToHourUtc = 13;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisabledSetupTypes",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "MaxExpectedCostR",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "MaxHoldingHoursIntraday",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "MaxHoldingHoursSwing",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "MinSizeMultiplierProduct",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "ShortEntryScorePenalty",
                table: "EngineSettings");
        }
    }
}
