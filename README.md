# MMW

Trading journal và deterministic intraday trading engine trên ASP.NET Core 8.

## Yêu cầu

- .NET SDK 8
- SQL Server cục bộ
- EF Core CLI: `dotnet tool restore` hoặc `dotnet-ef` khả dụng

AI và Binance API key không bắt buộc để build, test hoặc chạy đường quyết định tất định.

## Chạy local

```powershell
dotnet restore
dotnet ef database update `
  --project src/MMW.Infrastructure/MMW.Infrastructure.csproj `
  --startup-project src/MMW.Web/MMW.Web.csproj
dotnet run --project src/MMW.Web/MMW.Web.csproj
```

Mặc định: `http://localhost:5142`.

Database mới không có tài khoản mặc định. Cấu hình `BootstrapAdmin` bằng User Secrets trước lần chạy đầu tiên.

## Cấu hình an toàn

`src/MMW.Web/appsettings.json` phải giữ:

```json
"LiveTrading": {
  "Enabled": false,
  "UseTestnet": true
}
```

Đặt bí mật bằng User Secrets, không ghi giá trị thật vào `appsettings.json`:

```powershell
dotnet user-secrets set "BootstrapAdmin:Username" "<admin-user>" --project src/MMW.Web
dotnet user-secrets set "BootstrapAdmin:Password" "<strong-password>" --project src/MMW.Web
dotnet user-secrets set "AiService:Provider" "Claude" --project src/MMW.Web
dotnet user-secrets set "AiService:BaseUrl" "https://api.anthropic.com" --project src/MMW.Web
dotnet user-secrets set "AiService:Model" "claude-sonnet-4-20250514" --project src/MMW.Web
dotnet user-secrets set "AiService:ApiKey" "<key>" --project src/MMW.Web
```

Database mới chỉ tạo admin khi cả hai giá trị `BootstrapAdmin` được cấp từ cấu hình ngoài source. Không commit credential thật vào `appsettings.json`.

## Build và test

```powershell
dotnet build MMW.sln --configuration Release
dotnet test MMW.sln --configuration Release --no-build
```

Chạy riêng các nhóm quan trọng:

```powershell
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~TimeGuard"
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~Scoring|FullyQualifiedName~Determinism"
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~Backtest"
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~Ai"
```

## Nạp kho nến và funding

Kho lịch sử dùng để backtest offline. Nạp ít nhất các timeframe `15m`, `4h`, `1d`:

```powershell
dotnet run --project src/MMW.Web/MMW.Web.csproj -- `
  backfill `
  --symbols BTCUSDT,ETHUSDT `
  --intervals 15m,4h,1d `
  --from 2024-01-01 `
  --to 2026-01-01
```

Lệnh bất biến: chạy lại cùng khoảng không tạo nến trùng. Dùng `/Backtest` để xem khoảng trống dữ liệu trước khi chạy.

Funding và nến đều được nạp theo trang từ mốc `--from`; chạy lại cùng khoảng là bất biến và không tạo bản ghi trùng.

## Chạy backtest

Backtest chạy bằng CLI để thay `IClock` và market-data provider trong một scope riêng:

```powershell
dotnet run --project src/MMW.Web/MMW.Web.csproj -- `
  backtest `
  --account 1 `
  --symbol BTCUSDT `
  --from 2024-01-01 `
  --to 2026-01-01
```

Xem báo cáo tại `/Backtest`. Báo cáo hợp lệ phải có `Limitations`, phí, trượt giá, win rate, expectancy R, drawdown và phân rã theo giờ/trạng thái ngày.

## Luồng vận hành

- `/DailyPlan`: kế hoạch ngày.
- `/TimeGuard`: lịch và blackout.
- `/Scorecard`: quyết định tất định.
- `/Backtest`: báo cáo lịch sử.
- `/ShadowComparison`: so sánh AI shadow với engine tất định.

Chi tiết kiến trúc: [SYSTEM_OVERVIEW.md](./SYSTEM_OVERVIEW.md).
Checklist nghiệm thu: [specs/002-deterministic-entry-engine/quickstart.md](./specs/002-deterministic-entry-engine/quickstart.md).
