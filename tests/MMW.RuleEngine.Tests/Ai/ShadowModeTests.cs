using MMW.Application.Interfaces;
using MMW.Application.Services;
using MMW.Domain.Entities;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

public class ShadowModeTests
{
    [Fact]
    public async Task Duong_AI_de_lai_audit_nhung_khong_tao_Trade()
    {
        using var h = await ShadowModeHarness.CreateAsync(shadowEnabled: true);
        Assert.True(Assert.Single(await h.ReadAsync<AppSetting>()).ShadowComparisonEnabled);

        await h.ScanAsync();

        Assert.True(h.Llm.CallCount > 0, string.Join(Environment.NewLine, h.Logs));
        Assert.Single(await h.ReadAsync<AiSignalScanRecord>());
        Assert.Single(await h.ReadAsync<TradeSignal>());
        Assert.Empty(await h.ReadAsync<Trade>());
    }

    [Fact]
    public async Task Tat_shadow_thi_khong_goi_AI_va_khong_ghi_audit_AI()
    {
        using var h = await ShadowModeHarness.CreateAsync(shadowEnabled: false);

        await h.ScanAsync();

        Assert.Equal(0, h.Llm.CallCount);
        Assert.Empty(await h.ReadAsync<AiSignalScanRecord>());
        Assert.Empty(await h.ReadAsync<TradeSignal>());
    }

    [Fact]
    public void MarketScan_khong_con_nhan_cac_service_co_quyen_tao_hay_gui_lenh()
    {
        var parameters = typeof(MarketScanService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(ITradeService));
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(ILiveOrderService));
    }
}
