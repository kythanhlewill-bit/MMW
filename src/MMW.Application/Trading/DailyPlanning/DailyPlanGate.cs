using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

/// <summary>
/// Cổng kiểm tra kế hoạch ngày trước khi cho phép một lệnh mới (FR-021, FR-023).
/// </summary>
/// <remarks>
/// Chữ ký KHÔNG nhận điểm số, và đó là chủ ý. FR-021 nói lệnh ngược chiều bị từ chối "bất kể
/// điểm số"; cách chắc chắn nhất để điều đó đúng mãi mãi là không có chỗ nào nhét điểm vào.
/// </remarks>
public static class DailyPlanGate
{
    /// <summary>Null nghĩa là qua cổng. Khác null là lý do từ chối.</summary>
    public static VetoReason? Check(DailyPlan? plan, TradeDirection direction)
    {
        // FR-023: không có kế hoạch thì chặn hết. Không có nhánh nào dựng kế hoạch mặc định.
        if (plan is null) return VetoReason.NoDailyPlan;

        if (!IsAllowed(plan.AllowedDirections, direction)) return VetoReason.DirectionNotAllowed;
        if (plan.MaxTradesToday <= 0) return VetoReason.MaxTradesReached;

        return null;
    }

    public static bool IsAllowed(AllowedDirections allowed, TradeDirection direction) => direction switch
    {
        TradeDirection.Long => allowed is AllowedDirections.LongOnly or AllowedDirections.Both,
        TradeDirection.Short => allowed is AllowedDirections.ShortOnly or AllowedDirections.Both,
        _ => false,
    };
}
