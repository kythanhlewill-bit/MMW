using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntradayRegimeOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EffectiveDayRegime",
                table: "EntryScorecards",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntradayRegimeReason",
                table: "EntryScorecards",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIntradayRegimeOverride",
                table: "EntryScorecards",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EffectiveDayRegime",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "IntradayRegimeReason",
                table: "EntryScorecards");

            migrationBuilder.DropColumn(
                name: "IsIntradayRegimeOverride",
                table: "EntryScorecards");
        }
    }
}
