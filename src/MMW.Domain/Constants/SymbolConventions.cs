namespace MMW.Domain.Constants;

/// <summary>
/// Tách mã hợp đồng của sàn thành tài sản GỐC và tài sản ĐỊNH GIÁ.
/// </summary>
/// <remarks>
/// Sàn không có dấu phân cách trong mã: <c>BTCUSDT</c> là một chuỗi liền. Trước đây điều đó
/// không quan trọng vì cả hệ chỉ chạy trên các cặp đuôi USDT, và số dư được đọc bằng một bộ
/// lọc ghi cứng <c>asset == "USDT"</c>.
///
/// Nó thành quan trọng ngay khi có cặp thứ hai: Binance USDⓈ-M ở chế độ ký quỹ ĐƠN tài sản
/// (<c>multiAssetsMargin = false</c>, đúng cấu hình đang chạy) giữ ví USDT và ví USDC RIÊNG
/// biệt — một vị thế BTCUSDC không đụng được một đồng nào trong ví USDT. Tính cỡ lệnh cho
/// BTCUSDC theo số dư ví USDT là tính theo một túi tiền mà lệnh đó không tiêu được.
/// </remarks>
public static class SymbolConventions
{
    /// <summary>Tài sản định giá mặc định khi không suy ra được từ mã.</summary>
    public const string DefaultQuoteAsset = "USDT";

    /// <summary>
    /// Các đuôi được thử, XẾP THEO ĐỘ DÀI GIẢM DẦN.
    /// </summary>
    /// <remarks>
    /// Thứ tự là bắt buộc chứ không phải cho gọn. Thử theo thứ tự bảng chữ cái thì
    /// <c>BTCUSD1</c> khớp <c>USD</c> trước khi kịp thử <c>USD1</c>, và tài sản định giá đọc
    /// ra sẽ là một chuỗi không tồn tại trên sàn.
    /// </remarks>
    private static readonly string[] QuoteAssets =
    [
        "FDUSD", "TUSD", "BUSD", "USDT", "USDC", "USD1", "BNB", "BTC", "ETH", "USD",
    ];

    /// <summary>Tài sản định giá của mã — cũng chính là ví trả ký quỹ cho vị thế đó.</summary>
    public static string QuoteAssetOf(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return DefaultQuoteAsset;

        var s = symbol.Trim().ToUpperInvariant();

        foreach (var quote in QuoteAssets)
        {
            // Phải còn lại phần gốc. "USDT" trần không phải một cặp, và trả về gốc rỗng thì
            // mọi thứ đọc nó sau này đều nhận một chuỗi vô nghĩa mà không có lỗi nào nổ ra.
            if (s.Length > quote.Length && s.EndsWith(quote, StringComparison.Ordinal))
                return quote;
        }

        return DefaultQuoteAsset;
    }

    /// <summary>
    /// Tài sản gốc của mã. <c>BTCUSDT</c> và <c>BTCUSDC</c> cùng trả về <c>BTC</c>.
    /// </summary>
    /// <remarks>
    /// Đây là thứ cho phép nhận ra hai vị thế trên hai mã KHÁC NHAU thực chất là cùng một
    /// phơi nhiễm. Giá BTCUSDT và BTCUSDC bám nhau trong khoảng vài phần vạn, nên long mã này
    /// và short mã kia là tự hedge chính mình — trả phí hai chân để giữ một vị thế ròng gần
    /// bằng không. So theo chuỗi mã thì không có cách nào thấy điều đó.
    /// </remarks>
    public static string BaseAssetOf(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return string.Empty;

        var s = symbol.Trim().ToUpperInvariant();
        var quote = QuoteAssetOf(s);

        return s.EndsWith(quote, StringComparison.Ordinal) && s.Length > quote.Length
            ? s[..^quote.Length]
            : s;
    }

    /// <summary>Hai mã có cùng tài sản gốc không (khác đuôi định giá vẫn tính là cùng).</summary>
    public static bool SameBaseAsset(string? a, string? b)
    {
        var baseA = BaseAssetOf(a);
        return baseA.Length > 0
               && string.Equals(baseA, BaseAssetOf(b), StringComparison.Ordinal);
    }
}
