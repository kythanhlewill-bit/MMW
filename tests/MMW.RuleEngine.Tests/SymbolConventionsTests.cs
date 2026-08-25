using MMW.Domain.Constants;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Tách mã thành gốc/định giá. Đây là thứ quyết định lệnh nào tính cỡ theo ví nào, nên một lần
/// đọc sai ở đây không nổ ra lỗi — nó chỉ làm mọi lệnh sau đó vào sai cỡ trong im lặng.
/// </summary>
public class SymbolConventionsTests
{
    [Theory]
    [InlineData("BTCUSDT", "USDT")]
    [InlineData("BTCUSDC", "USDC")]
    [InlineData("ETHUSDC", "USDC")]
    [InlineData("btcusdc", "USDC")]
    [InlineData("  ETHUSDT  ", "USDT")]
    public void QuoteAssetOf_reads_the_settlement_wallet(string symbol, string expected)
        => Assert.Equal(expected, SymbolConventions.QuoteAssetOf(symbol));

    /// <summary>
    /// Đuôi dài phải được thử trước đuôi ngắn. Thử "USD" trước "USD1" thì BTCUSD1 đọc ra một
    /// tài sản định giá không tồn tại trên sàn, và lời gọi số dư sẽ trả null vĩnh viễn.
    /// </summary>
    [Theory]
    [InlineData("BTCUSD1", "USD1")]
    [InlineData("BTCFDUSD", "FDUSD")]
    [InlineData("ETHTUSD", "TUSD")]
    public void QuoteAssetOf_prefers_the_longest_matching_suffix(string symbol, string expected)
        => Assert.Equal(expected, SymbolConventions.QuoteAssetOf(symbol));

    /// <summary>
    /// Không suy ra được thì rơi về USDT — giữ nguyên hành vi trước khi có cặp USDC, chứ không
    /// trả chuỗi rỗng rồi để lời gọi sàn thất bại ở tầng dưới.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("USDT")]      // trần một tài sản, không phải một cặp
    [InlineData("SOMETHING")]
    public void QuoteAssetOf_falls_back_to_usdt(string? symbol)
        => Assert.Equal("USDT", SymbolConventions.QuoteAssetOf(symbol));

    [Theory]
    [InlineData("BTCUSDT", "BTC")]
    [InlineData("BTCUSDC", "BTC")]
    [InlineData("ETHUSDC", "ETH")]
    public void BaseAssetOf_strips_the_quote(string symbol, string expected)
        => Assert.Equal(expected, SymbolConventions.BaseAssetOf(symbol));

    /// <summary>
    /// Đây là lý do tồn tại của <c>BaseAssetOf</c>: long BTCUSDT và short BTCUSDC là tự hedge
    /// chính mình — hai chân phí để giữ một vị thế ròng gần bằng không. So theo chuỗi mã thì hai
    /// cái đó trông như hai mã độc lập.
    /// </summary>
    [Fact]
    public void SameBaseAsset_sees_through_the_quote_currency()
    {
        Assert.True(SymbolConventions.SameBaseAsset("BTCUSDT", "BTCUSDC"));
        Assert.True(SymbolConventions.SameBaseAsset("ETHUSDT", "ETHUSDC"));
        Assert.False(SymbolConventions.SameBaseAsset("BTCUSDT", "ETHUSDC"));
    }

    /// <summary>Mã rỗng không "giống" bất kỳ mã nào, kể cả một mã rỗng khác.</summary>
    [Fact]
    public void SameBaseAsset_never_matches_an_empty_symbol()
    {
        Assert.False(SymbolConventions.SameBaseAsset(null, null));
        Assert.False(SymbolConventions.SameBaseAsset("", "BTCUSDT"));
    }
}
