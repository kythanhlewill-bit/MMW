using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdaptiveSidewaysV6 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SetupEventId",
                table: "EntryScorecards",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetupQualityScore",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupSizeMultiplier",
                table: "EntryScorecards",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "SetupStage",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "V6BreakoutBufferAtr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.10m);

            migrationBuilder.AddColumn<int>(
                name: "V6BreakoutFreshBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "V6BreakoutMaxCostToTargetPercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 12m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6BreakoutMinNetRiskReward",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.30m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6BreakoutMinRelativeVolume",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.20m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6CompressionRiskCap",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.70m);

            migrationBuilder.AddColumn<int>(
                name: "V6MinSetupQuality",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<decimal>(
                name: "V6PatternContainmentPercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 80m);

            migrationBuilder.AddColumn<int>(
                name: "V6PatternLookbackBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 32);

            migrationBuilder.AddColumn<int>(
                name: "V6PatternMinTouchesPerSide",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "V6QualityFullMultiplier",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.75m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6QualityLowMultiplier",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.50m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6QualityMaxMultiplier",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RangeConfirmationMinRelativeVolume",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.80m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RangeMaxCostToTargetPercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 15m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RangeMinNetRiskReward",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RangeRiskCap",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.60m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RangeStopBufferAtr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.20m);

            migrationBuilder.AddColumn<int>(
                name: "V6RangeSweepLookbackBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RectangleMaxDriftAtr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.75m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RectangleMaxWidthAtr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 8m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6RectangleMinWidthAtr",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<int>(
                name: "V6SetupQualityFull",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 70);

            migrationBuilder.AddColumn<int>(
                name: "V6SetupQualityMax",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 85);

            migrationBuilder.AddColumn<decimal>(
                name: "V6TrendRiskCap",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.00m);

            migrationBuilder.AddColumn<decimal>(
                name: "V6TriangleMaxEndWidthFraction",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.70m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SetupEventId",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "SetupQualityScore",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "SetupSizeMultiplier",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "SetupStage",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "V6BreakoutBufferAtr",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6BreakoutFreshBars",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6BreakoutMaxCostToTargetPercent",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6BreakoutMinNetRiskReward",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6BreakoutMinRelativeVolume",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6CompressionRiskCap",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6MinSetupQuality",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6PatternContainmentPercent",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6PatternLookbackBars",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6PatternMinTouchesPerSide",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6QualityFullMultiplier",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6QualityLowMultiplier",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6QualityMaxMultiplier",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RangeConfirmationMinRelativeVolume",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RangeMaxCostToTargetPercent",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RangeMinNetRiskReward",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RangeRiskCap",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RangeStopBufferAtr",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RangeSweepLookbackBars",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RectangleMaxDriftAtr",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RectangleMaxWidthAtr",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6RectangleMinWidthAtr",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6SetupQualityFull",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6SetupQualityMax",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6TrendRiskCap",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V6TriangleMaxEndWidthFraction",
                table: "EngineSettings");
        }
    }
}
