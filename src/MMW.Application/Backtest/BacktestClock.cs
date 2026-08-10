using MMW.Application.Abstractions;

namespace MMW.Application.Backtest;

/// <summary>
/// Đồng hồ của một lần chạy kiểm thử lịch sử. Một trong hai cổng duy nhất bị thay so với
/// chạy thật — cổng còn lại là <c>IMarketDataProvider</c>.
/// </summary>
/// <remarks>
/// <see cref="Advance"/> ném khi bị gọi với thời điểm lùi về quá khứ. Thời gian đi lùi trong
/// một lần chạy là dấu hiệu chắc chắn của lỗi nhìn trước tương lai: nó nghĩa là vòng lặp đã
/// nhảy về một mốc mà nó vừa "biết" tương lai của mốc đó. Phải nổ ngay chứ không được âm thầm
/// cho ra một kết quả đẹp.
/// </remarks>
public sealed class BacktestClock : IClock
{
    public BacktestClock(DateTime startUtc) => UtcNow = AsUtc(startUtc, nameof(startUtc));

    public DateTime UtcNow { get; private set; }

    /// <summary>Đẩy đồng hồ tới. Chỉ tiến, không lùi. Đứng yên thì chấp nhận.</summary>
    public void Advance(DateTime toUtc)
    {
        var target = AsUtc(toUtc, nameof(toUtc));

        if (target < UtcNow)
        {
            throw new InvalidOperationException(
                $"Đồng hồ kiểm thử không được lùi: đang ở {UtcNow:O}, bị đẩy về {target:O}. " +
                "Thời gian đi lùi trong một lần chạy nghĩa là vòng lặp đang dùng dữ liệu của tương lai.");
        }

        UtcNow = target;
    }

    private static DateTime AsUtc(DateTime value, string paramName) => value.Kind switch
    {
        DateTimeKind.Local => throw new ArgumentException(
            "Cần thời điểm UTC, nhận được giờ địa phương.", paramName),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
