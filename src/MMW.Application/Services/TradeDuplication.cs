namespace MMW.Application.Services;

/// <summary>
/// Quy tắc nhận diện lệnh "trùng tương đối" để tránh mở 2 vị thế gần giống nhau
/// (cùng symbol + cùng hướng + giá vào xấp xỉ).
/// </summary>
internal static class TradeDuplication
{
    /// <summary>Ngưỡng coi là cùng vùng giá (%). Có thể chỉnh nếu cần lỏng/chặt hơn.</summary>
    public const decimal PriceTolerancePercent = 0.5m;

    /// <summary>True nếu hai giá vào lệch ≤ ngưỡng %.</summary>
    public static bool IsNearPrice(decimal existingEntry, decimal newEntry)
    {
        if (newEntry <= 0m) return false;
        return Math.Abs(existingEntry - newEntry) / newEntry * 100m <= PriceTolerancePercent;
    }
}
