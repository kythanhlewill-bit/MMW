namespace MMW.Application.Trading;

/// <summary>
/// Chạy SONG SONG bộ luật trong ngày và bộ luật swing khung 4 giờ trên cùng một tài khoản.
/// </summary>
/// <remarks>
/// <para><b>Vì sao trước đây không làm được.</b> <c>SetupTriggerPolicy</c> rẽ theo
/// <c>StrategyVersion</c> của tài khoản và trả về ngay khi gặp bộ luật swing, nên năm nhánh
/// trong ngày không bao giờ chạy tới. Đó không phải thiếu sót: hai bộ luật đọc CHIỀU từ hai
/// nguồn khác nhau — bộ ngắn đọc từ kế hoạch ngày của mã dẫn dắt, bộ swing đọc từ cấu trúc 4h
/// của chính mã — nên trên cùng một mã chúng có thể ra hai chiều ngược nhau.</para>
///
/// <para><b>Cách gỡ.</b> Không cho hai bộ luật gặp nhau trên cùng một mã. Bộ ngắn giữ các cặp
/// đuôi USDT, bộ swing chạy các cặp đuôi USDC của cùng tài sản gốc. Chúng là hai hợp đồng khác
/// nhau trên sàn, và quan trọng hơn: ở chế độ ký quỹ ĐƠN tài sản, ví USDT và ví USDC là hai túi
/// riêng biệt. Mỗi bộ luật có ngân sách ký quỹ riêng ngay tại sàn, không cần tài khoản thứ hai
/// và không cái nào ăn được ký quỹ của cái kia.</para>
///
/// <para><b>Cái giá.</b> BTCUSDT và BTCUSDC bám giá nhau trong khoảng vài phần vạn — cùng một
/// tài sản. Long một mã và short mã kia là tự hedge chính mình: phơi nhiễm ròng gần bằng không
/// mà vẫn trả phí và funding cho cả hai chân. Xem <c>CrossQuoteExposureGate</c>, nơi chuyện đó
/// bị chặn.</para>
///
/// <para>Để trong cấu hình ứng dụng chứ không thành cột trong CSDL là có chủ ý: đây là một
/// công tắc thử nghiệm, và nó phải tắt/bật được bằng biến môi trường mà không cần migration.</para>
/// </remarks>
public sealed class DualEngineOptions
{
    public const string SectionName = "DualEngine";

    /// <summary>
    /// Công tắc tổng. false = giữ nguyên hành vi cũ, mỗi tài khoản đúng một bộ luật.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Các mã dành cho bộ luật swing 4h. Bỏ trống thì đường swing không chạy dù đã bật.
    /// </summary>
    /// <remarks>
    /// PHẢI không giao với danh sách mã của tài khoản. Trùng một mã nghĩa là hai bộ luật lại
    /// gặp nhau trên đúng cái mã đó, và toàn bộ lý do tách bằng tài sản định giá mất hiệu lực.
    /// <see cref="Validate"/> cưỡng chế điều này lúc khởi động.
    /// </remarks>
    public string HtfSymbols { get; set; } = "";

    /// <summary>Phiên bản bộ luật dùng cho đường swing. Giữ ở cấu hình để còn nâng cấp được.</summary>
    public int HtfStrategyVersion { get; set; } = 7;

    public IReadOnlyList<string> HtfSymbolList() =>
        HtfSymbols
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Lý do không dùng được, hoặc <c>null</c> nếu hợp lệ. Gọi lúc dựng cấu hình cho đường swing.
    /// </summary>
    public string? Validate(IReadOnlyList<string> intradaySymbols)
    {
        if (!Enabled) return null;

        var htf = HtfSymbolList();
        if (htf.Count == 0)
            return "DualEngine bật nhưng HtfSymbols trống — không có mã nào cho đường swing.";

        var overlap = htf.Intersect(intradaySymbols, StringComparer.OrdinalIgnoreCase).ToList();
        if (overlap.Count > 0)
        {
            return $"HtfSymbols trùng mã với danh sách của tài khoản ({string.Join(", ", overlap)}) — "
                 + "hai bộ luật sẽ gặp nhau trên cùng một mã, đúng thứ mà cách tách này sinh ra để tránh.";
        }

        return null;
    }
}
