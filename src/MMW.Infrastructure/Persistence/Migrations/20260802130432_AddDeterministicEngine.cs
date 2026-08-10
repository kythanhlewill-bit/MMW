using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMW.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeterministicEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EntryScorecardId",
                table: "Trades",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeterministicEngineEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShadowComparisonEnabled",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "BacktestRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Symbols = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EngineSettingSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TradeCount = table.Column<int>(type: "int", nullable: false),
                    WinRate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ExpectancyR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaxDrawdownPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    LongestLossStreak = table.Column<int>(type: "int", nullable: false),
                    TotalFees = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    TotalSlippage = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    BreakdownByHourJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BreakdownByRegimeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Limitations = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradingAccountId = table.Column<long>(type: "bigint", nullable: false),
                    PlanDateUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DayRegime = table.Column<int>(type: "int", nullable: false),
                    VolatilityRegime = table.Column<int>(type: "int", nullable: false),
                    AllowedDirections = table.Column<int>(type: "int", nullable: false),
                    RiskMultiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaxTradesToday = table.Column<int>(type: "int", nullable: false),
                    PreviousDayHigh = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PreviousDayLow = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    WeeklyOpen = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    DailyOpen = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    BtcStructure = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AtrPercentile = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    FundingRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    OpenInterestChange24hPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    LongShortAccountRatio = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    FearGreedIndex = table.Column<int>(type: "int", nullable: true),
                    MissingInputs = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsComplete = table.Column<bool>(type: "bit", nullable: false),
                    AiDayRiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AiNarrative = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AiConfidence = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    AiAnswered = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyPlans_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EngineSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradingAccountId = table.Column<long>(type: "bigint", nullable: false),
                    MinScoreToEnter = table.Column<int>(type: "int", nullable: false),
                    ScoreThresholdFull = table.Column<int>(type: "int", nullable: false),
                    ScoreThresholdMax = table.Column<int>(type: "int", nullable: false),
                    SizeMultiplierLow = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SizeMultiplierFull = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SizeMultiplierMax = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    WeightTechnical = table.Column<int>(type: "int", nullable: false),
                    WeightMarket = table.Column<int>(type: "int", nullable: false),
                    WeightLiquidity = table.Column<int>(type: "int", nullable: false),
                    SwingPivotBars = table.Column<int>(type: "int", nullable: false),
                    RetestWindowBars = table.Column<int>(type: "int", nullable: false),
                    MaxAtrFromConfirmation = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    EntryTimeframe = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    BiasTimeframe = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    PersonalStatsMinClosedTrades = table.Column<int>(type: "int", nullable: false),
                    WorstHoursPenalty = table.Column<int>(type: "int", nullable: false),
                    LossStreakSizeHalveAt = table.Column<int>(type: "int", nullable: false),
                    RevengeBlockMinutes = table.Column<int>(type: "int", nullable: false),
                    OversizeBlockMultiple = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    OversizeLookbackTrades = table.Column<int>(type: "int", nullable: false),
                    AiBlackoutMaxMinutes = table.Column<int>(type: "int", nullable: false),
                    AiContextDefaultTtlMinutes = table.Column<int>(type: "int", nullable: false),
                    BacktestTakerFeePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    BacktestEntrySlippageBps = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    BacktestStopSlippageBps = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    ShadowAiComparisonEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngineSettings_TradingAccounts_TradingAccountId",
                        column: x => x.TradingAccountId,
                        principalTable: "TradingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FundingRateArchives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FundingTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FundingRate = table.Column<decimal>(type: "decimal(9,8)", precision: 9, scale: 8, nullable: false),
                    MarkPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundingRateArchives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KlineArchives",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Interval = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    OpenTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CloseTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    QuoteVolume = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    TradeCount = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KlineArchives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketContextRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Leaning = table.Column<int>(type: "int", nullable: false),
                    AffectedSymbols = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Narrative = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRumor = table.Column<bool>(type: "bit", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RawResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RejectedFields = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketContextRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccursAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    SourceKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntryScorecards",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradingAccountId = table.Column<long>(type: "bigint", nullable: false),
                    DailyPlanId = table.Column<long>(type: "bigint", nullable: true),
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Interval = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CandleCloseTimeUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: true),
                    TechnicalScore = table.Column<int>(type: "int", nullable: false),
                    MarketScore = table.Column<int>(type: "int", nullable: false),
                    LiquidityScore = table.Column<int>(type: "int", nullable: false),
                    DisciplinePenalty = table.Column<int>(type: "int", nullable: false),
                    TotalScore = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    VetoReason = table.Column<int>(type: "int", nullable: true),
                    VetoDetail = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BaseSizeR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    DayRiskMultiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    DisciplineMultiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    AiMultiplier = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    FinalSizeR = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SuggestedEntry = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    SuggestedStopLoss = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    SuggestedTakeProfit = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    RiskReward = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    TradeId = table.Column<long>(type: "bigint", nullable: true),
                    InputSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsBacktest = table.Column<bool>(type: "bit", nullable: false),
                    BacktestRunId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryScorecards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryScorecards_DailyPlans_DailyPlanId",
                        column: x => x.DailyPlanId,
                        principalTable: "DailyPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BlackoutRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EngineSettingId = table.Column<long>(type: "bigint", nullable: false),
                    EventKind = table.Column<int>(type: "int", nullable: false),
                    MinutesBefore = table.Column<int>(type: "int", nullable: false),
                    MinutesAfter = table.Column<int>(type: "int", nullable: false),
                    BlocksNewEntries = table.Column<bool>(type: "bit", nullable: false),
                    RequiresPositionAction = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlackoutRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlackoutRules_EngineSettings_EngineSettingId",
                        column: x => x.EngineSettingId,
                        principalTable: "EngineSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionQualityRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EngineSettingId = table.Column<long>(type: "bigint", nullable: false),
                    FromHourUtc = table.Column<int>(type: "int", nullable: false),
                    ToHourUtc = table.Column<int>(type: "int", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionQualityRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionQualityRows_EngineSettings_EngineSettingId",
                        column: x => x.EngineSettingId,
                        principalTable: "EngineSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntryScorecardLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryScorecardId = table.Column<long>(type: "bigint", nullable: false),
                    CriterionKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Group = table.Column<int>(type: "int", nullable: false),
                    MaxPoints = table.Column<int>(type: "int", nullable: false),
                    AwardedPoints = table.Column<int>(type: "int", nullable: false),
                    IsHardVeto = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DataAvailable = table.Column<bool>(type: "bit", nullable: false),
                    IsApproximation = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedUser = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryScorecardLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EntryScorecardLines_EntryScorecards_EntryScorecardId",
                        column: x => x.EntryScorecardId,
                        principalTable: "EntryScorecards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlackoutRules_EngineSettingId_EventKind",
                table: "BlackoutRules",
                columns: new[] { "EngineSettingId", "EventKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyPlans_TradingAccountId_PlanDateUtc",
                table: "DailyPlans",
                columns: new[] { "TradingAccountId", "PlanDateUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EngineSettings_TradingAccountId",
                table: "EngineSettings",
                column: "TradingAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryScorecardLines_CriterionKey",
                table: "EntryScorecardLines",
                column: "CriterionKey");

            migrationBuilder.CreateIndex(
                name: "IX_EntryScorecardLines_EntryScorecardId_CriterionKey",
                table: "EntryScorecardLines",
                columns: new[] { "EntryScorecardId", "CriterionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntryScorecards_BacktestRunId",
                table: "EntryScorecards",
                column: "BacktestRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryScorecards_DailyPlanId",
                table: "EntryScorecards",
                column: "DailyPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_EntryScorecards_Symbol_CandleCloseTimeUtc_IsBacktest",
                table: "EntryScorecards",
                columns: new[] { "Symbol", "CandleCloseTimeUtc", "IsBacktest" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundingRateArchives_Symbol_FundingTimeUtc",
                table: "FundingRateArchives",
                columns: new[] { "Symbol", "FundingTimeUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KlineArchives_Symbol_Interval_OpenTimeUtc",
                table: "KlineArchives",
                columns: new[] { "Symbol", "Interval", "OpenTimeUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketContextRecords_ExpiresAtUtc",
                table: "MarketContextRecords",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarketContextRecords_SourceKey",
                table: "MarketContextRecords",
                column: "SourceKey",
                unique: true,
                filter: "[SourceKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_OccursAtUtc",
                table: "ScheduledEvents",
                column: "OccursAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEvents_SourceKey",
                table: "ScheduledEvents",
                column: "SourceKey",
                unique: true,
                filter: "[SourceKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SessionQualityRows_EngineSettingId_FromHourUtc",
                table: "SessionQualityRows",
                columns: new[] { "EngineSettingId", "FromHourUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestRuns");

            migrationBuilder.DropTable(
                name: "BlackoutRules");

            migrationBuilder.DropTable(
                name: "EntryScorecardLines");

            migrationBuilder.DropTable(
                name: "FundingRateArchives");

            migrationBuilder.DropTable(
                name: "KlineArchives");

            migrationBuilder.DropTable(
                name: "MarketContextRecords");

            migrationBuilder.DropTable(
                name: "ScheduledEvents");

            migrationBuilder.DropTable(
                name: "SessionQualityRows");

            migrationBuilder.DropTable(
                name: "EntryScorecards");

            migrationBuilder.DropTable(
                name: "EngineSettings");

            migrationBuilder.DropTable(
                name: "DailyPlans");

            migrationBuilder.DropColumn(
                name: "EntryScorecardId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "DeterministicEngineEnabled",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "ShadowComparisonEnabled",
                table: "AppSettings");
        }
    }
}
