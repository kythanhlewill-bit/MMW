using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionTargetsV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedFirstTakeProfit",
                table: "EntryScorecards",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedLimitEntry",
                table: "EntryScorecards",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SuggestedRunnerTakeProfit",
                table: "EntryScorecards",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedFirstTakeProfit",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "SuggestedLimitEntry",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "SuggestedRunnerTakeProfit",
                table: "EntryScorecards");
        }
    }
}
