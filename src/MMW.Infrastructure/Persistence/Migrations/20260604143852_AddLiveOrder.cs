using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExchangeClientOrderId",
                table: "Trades",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExchangeOrderId",
                table: "Trades",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLive",
                table: "Trades",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LiveNote",
                table: "Trades",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LiveStatus",
                table: "Trades",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeClientOrderId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "ExchangeOrderId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "IsLive",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "LiveNote",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "LiveStatus",
                table: "Trades");
        }
    }
}
