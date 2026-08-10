using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringSettings : Migration
    {
        /// <inheritdoc />
        /// <summary>Thêm các ngưỡng chấm điểm cấu hình được của tầng 3.</summary>
        /// <remarks>
        /// Các <c>defaultValue</c> được SỬA TAY khỏi 0 mà EF sinh ra, và phải khớp giá trị khởi
        /// tạo trong <c>EngineSetting</c>. Giá trị khởi tạo của thuộc tính C# chỉ áp cho thực thể
        /// MỚI, không áp cho dòng đã nằm sẵn trong cơ sở dữ liệu.
        ///
        /// Nguy hiểm nhất là <c>Symbols</c>: để rỗng thì <c>EvaluateAllAsync</c> duyệt qua một
        /// danh sách rỗng, engine không chấm điểm mã nào, và không có lỗi nào được ném ra.
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExtremeFundingRate",
                table: "EngineSettings",
                type: "decimal(9,8)",
                precision: 9,
                scale: 8,
                nullable: false,
                defaultValue: 0.0005m);

            migrationBuilder.AddColumn<decimal>(
                name: "LeaderCorrelationStrong",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.7m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxSpreadBps",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 2m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpenInterestStrongChangePercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 3m);

            migrationBuilder.AddColumn<decimal>(
                name: "RsiLowerBound",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 45m);

            migrationBuilder.AddColumn<decimal>(
                name: "RsiUpperBound",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 65m);

            migrationBuilder.AddColumn<decimal>(
                name: "StopAtrMultiple",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<string>(
                name: "Symbols",
                table: "EngineSettings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "BTCUSDT,ETHUSDT");

            migrationBuilder.AddColumn<decimal>(
                name: "VolatilitySweetSpotHigh",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 70m);

            migrationBuilder.AddColumn<decimal>(
                name: "VolatilitySweetSpotLow",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 30m);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeBreakoutMultiple",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtremeFundingRate",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "LeaderCorrelationStrong",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "MaxSpreadBps",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "OpenInterestStrongChangePercent",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "RsiLowerBound",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "RsiUpperBound",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "StopAtrMultiple",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "Symbols",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "VolatilitySweetSpotHigh",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "VolatilitySweetSpotLow",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "VolumeBreakoutMultiple",
                table: "EngineSettings");
        }
    }
}
