using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiContextSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiMaxNewsCallsPerDay",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<int>(
                name: "AiMaxNewsCallsPerRun",
                table: "EngineSettings",
                type: "int",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiMaxNewsCallsPerDay",
                table: "EngineSettings");

            migrationBuilder.DropColumn(
                name: "AiMaxNewsCallsPerRun",
                table: "EngineSettings");
        }
    }
}
