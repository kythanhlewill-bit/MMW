using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShadowAuditAndExpandBacktestLimitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Limitations",
                table: "BacktestRuns",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicDirection",
                table: "AiSignalScanRecords",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeterministicOutcome",
                table: "AiSignalScanRecords",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeterministicScore",
                table: "AiSignalScanRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisagreementReason",
                table: "AiSignalScanRecords",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EntryScorecardId",
                table: "AiSignalScanRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDisagreement",
                table: "AiSignalScanRecords",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSignalScanRecords_IsDisagreement_ScannedAt",
                table: "AiSignalScanRecords",
                columns: new[] { "IsDisagreement", "ScannedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiSignalScanRecords_IsDisagreement_ScannedAt",
                table: "AiSignalScanRecords");

            migrationBuilder.DropColumn(
                name: "DeterministicDirection",
                table: "AiSignalScanRecords");

            migrationBuilder.DropColumn(
                name: "DeterministicOutcome",
                table: "AiSignalScanRecords");

            migrationBuilder.DropColumn(
                name: "DeterministicScore",
                table: "AiSignalScanRecords");

            migrationBuilder.DropColumn(
                name: "DisagreementReason",
                table: "AiSignalScanRecords");

            migrationBuilder.DropColumn(
                name: "EntryScorecardId",
                table: "AiSignalScanRecords");

            migrationBuilder.DropColumn(
                name: "IsDisagreement",
                table: "AiSignalScanRecords");

            migrationBuilder.AlterColumn<string>(
                name: "Limitations",
                table: "BacktestRuns",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
