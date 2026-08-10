using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryAndTriggerFirstV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCostR",
                table: "EntryScorecards",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetRiskReward",
                table: "EntryScorecards",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SetupType",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "StopDistanceBps",
                table: "EntryScorecards",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StrategyVersion",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "TriggerDetail",
                table: "EntryScorecards",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TriggerState",
                table: "EntryScorecards",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "StateCode",
                table: "EntryScorecardLines",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StrategyVersion",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "V3LockedNetRMin",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.25m);

            migrationBuilder.AddColumn<decimal>(
                name: "V3MaxCostToTargetPercent",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddColumn<decimal>(
                name: "V3MinImpulseVolumeMultiple",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "V3MinNetRiskReward",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1.5m);

            migrationBuilder.AddColumn<decimal>(
                name: "V3PullbackVolumeMaxFraction",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0.8m);

            migrationBuilder.AddColumn<decimal>(
                name: "V3RangeMinRelativeVolume",
                table: "EngineSettings",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "V3TriggerFreshBars",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "DecisionFingerprint",
                table: "BacktestRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DiagnosticsJson",
                table: "BacktestRuns",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "GrossExpectancyR",
                table: "BacktestRuns",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StrategyVersion",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "TelemetrySchemaVersion",
                table: "BacktestRuns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TradeFingerprint",
                table: "BacktestRuns",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedCostR",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "NetRiskReward",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "SetupType",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "StopDistanceBps",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "StrategyVersion",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "TriggerDetail",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "TriggerState",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "StateCode",
                table: "EntryScorecardLines");

            migrationBuilder.DropColumn(
                name: "StrategyVersion",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3LockedNetRMin",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3MaxCostToTargetPercent",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3MinImpulseVolumeMultiple",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3MinNetRiskReward",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3PullbackVolumeMaxFraction",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3RangeMinRelativeVolume",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "V3TriggerFreshBars",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "DecisionFingerprint",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "DiagnosticsJson",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "GrossExpectancyR",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "StrategyVersion",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "TelemetrySchemaVersion",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "TradeFingerprint",
                table: "BacktestRuns");
        }
    }
}
