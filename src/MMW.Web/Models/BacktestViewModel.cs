using MMW.Domain.Entities;

namespace MMW.Web.Models;

/// <summary>Dữ liệu cho màn hình kiểm thử lịch sử.</summary>
public class BacktestViewModel
{
    public IReadOnlyList<BacktestRun> Runs { get; set; } = Array.Empty<BacktestRun>();
    public BacktestRun? Selected { get; set; }

    public int ArchiveCandleCount { get; set; }
    public int ArchiveFundingCount { get; set; }
    public IReadOnlyList<string> ArchiveSymbols { get; set; } = Array.Empty<string>();

    /// <summary>Các dòng hạn chế của lần chạy đang xem.</summary>
    public IReadOnlyList<string> Limitations =>
        string.IsNullOrWhiteSpace(Selected?.Limitations)
            ? Array.Empty<string>()
            : Selected!.Limitations.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
}
