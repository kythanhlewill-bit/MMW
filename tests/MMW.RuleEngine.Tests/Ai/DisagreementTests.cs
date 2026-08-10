using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

public class DisagreementTests
{
    [Fact]
    public async Task AI_de_xuat_vao_lenh_nhung_duong_tat_dinh_tu_choi_thi_ghi_bat_dong()
    {
        using var h = await ShadowModeHarness.CreateAsync(shadowEnabled: true);
        await h.AddDeterministicScorecardAsync(ScorecardOutcome.Vetoed, TradeDirection.Long);

        await h.ScanAsync();

        var audit = Assert.Single(await h.ReadAsync<AiSignalScanRecord>());
        Assert.True(audit.IsDisagreement);
        Assert.Equal(ScorecardOutcome.Vetoed.ToString(), audit.DeterministicOutcome);
        Assert.NotNull(audit.EntryScorecardId);
        Assert.False(string.IsNullOrWhiteSpace(audit.DisagreementReason));
    }

    [Fact]
    public async Task Hai_duong_cung_de_xuat_cung_chieu_thi_khong_bat_dong()
    {
        using var h = await ShadowModeHarness.CreateAsync(shadowEnabled: true);
        await h.AddDeterministicScorecardAsync(ScorecardOutcome.Entered, TradeDirection.Long);

        await h.ScanAsync();

        var audit = Assert.Single(await h.ReadAsync<AiSignalScanRecord>());
        Assert.False(audit.IsDisagreement);
    }
}
