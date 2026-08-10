using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktestDiagnosticsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BreakdownByExitReasonJson",
                table: "BacktestRuns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BreakdownByModeJson",
                table: "BacktestRuns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ComparableTrialNumber",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectancyRCiHigh",
                table: "BacktestRuns",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectancyRCiLow",
                table: "BacktestRuns",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "StructuralRrDistributionJson",
                table: "BacktestRuns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StructuralRrVetoObservationsJson",
                table: "BacktestRuns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "WinRateCiHigh",
                table: "BacktestRuns",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WinRateCiLow",
                table: "BacktestRuns",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakdownByExitReasonJson",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "BreakdownByModeJson",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "ComparableTrialNumber",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "ExpectancyRCiHigh",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "ExpectancyRCiLow",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "StructuralRrDistributionJson",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "StructuralRrVetoObservationsJson",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "WinRateCiHigh",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "WinRateCiLow",
                table: "BacktestRuns");
        }
    }
}
