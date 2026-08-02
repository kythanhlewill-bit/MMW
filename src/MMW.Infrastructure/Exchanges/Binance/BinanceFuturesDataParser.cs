using System.Globalization;
using System.Text.Json;
using MMW.Application.MarketData.Models;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>
/// Bóc tách phản hồi của các endpoint futures công khai.
/// </summary>
/// <remarks>
/// Nguyên tắc xuyên suốt: <b>không bao giờ ném</b>. Phản hồi hỏng, thiếu trường, sai kiểu,
/// hay là phong bì lỗi đều trả <c>null</c> — theo hợp đồng lỗi ở <c>IMarketDataProvider</c>,
/// thiếu dữ liệu là điều kiện bình thường và tiêu chí liên quan nhận 0 điểm (FR-006).
///
/// Hai cạm bẫy đã kiểm chứng với API thật ở T001, đều phải xử lý ở đây:
/// <list type="number">
/// <item>Sàn có thể trả <b>đối tượng</b> ở nơi đang chờ <b>mảng</b>, kèm HTTP 200 —
/// phong bì lỗi phi tiêu chuẩn <c>{"status":"ERROR","code":"99099990",...}</c>.</item>
/// <item>Sàn <b>thêm trường mới</b> theo thời gian (<c>rateType</c>, <c>CMCCirculatingSupply</c>);
/// bóc tách phải bỏ qua trường lạ chứ không được coi là lỗi.</item>
/// </list>
/// </remarks>
public static class BinanceFuturesDataParser
{
    public static FundingSnapshot? ParseFunding(string json)
    {
        var root = TryParseObject(json);
        if (root is null) return null;

        var rate = GetDecimal(root.Value, "lastFundingRate");
        var mark = GetDecimal(root.Value, "markPrice");
        var next = GetUnixMs(root.Value, "nextFundingTime");
        var time = GetUnixMs(root.Value, "time");

        if (rate is null || mark is null || next is null) return null;

        return new FundingSnapshot(rate.Value, next.Value, mark.Value, time ?? next.Value);
    }

    public static IReadOnlyList<FundingRatePoint>? ParseFundingHistory(string json)
    {
        var items = TryParseArray(json);
        if (items is null) return null;

        var points = new List<FundingRatePoint>(items.Count);
        foreach (var e in items)
        {
            var t = GetUnixMs(e, "fundingTime");
            var r = GetDecimal(e, "fundingRate");
            if (t is null || r is null) return null;

            points.Add(new FundingRatePoint(t.Value, r.Value, GetDecimal(e, "markPrice")));
        }
        return points.Count == 0 ? null : points;
    }

    public static OpenInterestSeries? ParseOpenInterestHist(string symbol, string period, string json)
    {
        var items = TryParseArray(json);
        if (items is null) return null;

        var points = new List<OpenInterestPoint>(items.Count);
        foreach (var e in items)
        {
            var t = GetUnixMs(e, "timestamp");
            var oi = GetDecimal(e, "sumOpenInterest");
            var oiv = GetDecimal(e, "sumOpenInterestValue");
            if (t is null || oi is null || oiv is null) return null;

            points.Add(new OpenInterestPoint(t.Value, oi.Value, oiv.Value));
        }
        return points.Count == 0 ? null : new OpenInterestSeries(symbol, period, points);
    }

    /// <summary>Lấy điểm dữ liệu MỚI NHẤT — sàn trả chuỗi tăng dần theo thời gian.</summary>
    public static LongShortRatio? ParseLongShortRatio(string json)
    {
        var items = TryParseArray(json);
        if (items is null || items.Count == 0) return null;

        var last = items[^1];
        var ratio = GetDecimal(last, "longShortRatio");
        var lng = GetDecimal(last, "longAccount");
        var sht = GetDecimal(last, "shortAccount");
        var t = GetUnixMs(last, "timestamp");
        if (ratio is null || lng is null || sht is null || t is null) return null;

        return new LongShortRatio(ratio.Value, lng.Value, sht.Value, t.Value);
    }

    /// <summary>
    /// Lấy điểm mới nhất. Endpoint này KHÔNG trả trường <c>symbol</c> — khác hai endpoint
    /// <c>/futures/data/*</c> còn lại.
    /// </summary>
    public static TakerFlow? ParseTakerFlow(string json)
    {
        var items = TryParseArray(json);
        if (items is null || items.Count == 0) return null;

        var last = items[^1];
        var ratio = GetDecimal(last, "buySellRatio");
        var buy = GetDecimal(last, "buyVol");
        var sell = GetDecimal(last, "sellVol");
        var t = GetUnixMs(last, "timestamp");
        if (ratio is null || buy is null || sell is null || t is null) return null;

        return new TakerFlow(ratio.Value, buy.Value, sell.Value, t.Value);
    }

    public static DepthSnapshot? ParseDepth(string json)
    {
        var root = TryParseObject(json);
        if (root is null) return null;

        var bids = ParseDepthSide(root.Value, "bids");
        var asks = ParseDepthSide(root.Value, "asks");
        if (bids is null || asks is null) return null;

        var t = GetUnixMs(root.Value, "E") ?? GetUnixMs(root.Value, "T");
        return new DepthSnapshot(bids, asks, t ?? default);
    }

    private static List<DepthLevel>? ParseDepthSide(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;

        var levels = new List<DepthLevel>();
        foreach (var pair in arr.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2) return null;

            var price = ToDecimal(pair[0]);
            var qty = ToDecimal(pair[1]);
            if (price is null || qty is null) return null;

            levels.Add(new DepthLevel(price.Value, qty.Value));
        }
        return levels;
    }

    // ── Trợ giúp bóc tách an toàn ───────────────────────────────────────

    /// <summary>
    /// Trả về danh sách phần tử khi và chỉ khi phản hồi thực sự là một mảng JSON.
    /// Phản hồi dạng đối tượng ở đây chính là phong bì lỗi phi tiêu chuẩn của sàn.
    /// </summary>
    private static List<JsonElement>? TryParseArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

            var items = new List<JsonElement>();
            foreach (var e in doc.RootElement.EnumerateArray()) items.Add(e.Clone());
            return items;
        }
        catch (JsonException) { return null; }
    }

    private static JsonElement? TryParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            // Phong bì lỗi phi tiêu chuẩn: HTTP 200, dạng đối tượng, có "status":"ERROR".
            if (doc.RootElement.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                && string.Equals(status.GetString(), "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Phong bì lỗi chuẩn: {"code":-1130,"msg":"..."}
            if (doc.RootElement.TryGetProperty("code", out _) && doc.RootElement.TryGetProperty("msg", out _))
                return null;

            return doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    private static decimal? GetDecimal(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? ToDecimal(v) : null;

    private static decimal? ToDecimal(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null,
        JsonValueKind.Number => v.TryGetDecimal(out var n) ? n : null,
        _ => null,
    };

    private static DateTime? GetUnixMs(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;

        long ms;
        switch (v.ValueKind)
        {
            case JsonValueKind.Number when v.TryGetInt64(out var n): ms = n; break;
            case JsonValueKind.String when long.TryParse(v.GetString(), out var s): ms = s; break;
            default: return null;
        }

        // Ngoài dải hợp lệ của Unix ms ⟹ dữ liệu hỏng, không phải thời điểm.
        if (ms <= 0 || ms > 253_402_300_799_000) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }
}
