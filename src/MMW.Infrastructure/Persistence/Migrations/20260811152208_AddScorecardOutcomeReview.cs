using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScorecardOutcomeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScorecardOutcomeReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryScorecardId = table.Column<long>(type: "bigint", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolverVersion = table.Column<int>(type: "int", nullable: false),
                    BarInterval = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    HorizonBars = table.Column<int>(type: "int", nullable: false),
                    FirstBarUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ExitAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BarsToExit = table.Column<int>(type: "int", nullable: false),
                    GrossR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    FeeR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SlippageR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    FundingR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    NetR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    StopDistancePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaxFavorableExcursionR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaxAdverseExcursionR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScorecardOutcomeReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScorecardOutcomeReviews_EntryScorecards_EntryScorecardId",
                        column: x => x.EntryScorecardId,
                        principalTable: "EntryScorecards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScorecardOutcomeReviews_EntryScorecardId_ResolverVersion",
                table: "ScorecardOutcomeReviews",
                columns: new[] { "EntryScorecardId", "ResolverVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScorecardOutcomeReviews_ResolverVersion_Outcome_ResolvedAtUtc",
                table: "ScorecardOutcomeReviews",
                columns: new[] { "ResolverVersion", "Outcome", "ResolvedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScorecardOutcomeReviews");
        }
    }
}
