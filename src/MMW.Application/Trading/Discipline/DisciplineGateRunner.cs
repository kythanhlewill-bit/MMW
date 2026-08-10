using MMW.Application.Trading.Scoring;

namespace MMW.Application.Trading.Discipline;

/// <summary>Kết quả từng gate, giữ lại để ghi thành dòng phiếu chấm điểm.</summary>
public sealed record GateLine(string Key, GateResult Result);

/// <summary>Kết quả gộp của cả bộ gate, kèm chi tiết từng gate.</summary>
public sealed record DisciplineOutcome(GateAggregate Aggregate, IReadOnlyList<GateLine> Lines);

public interface IDisciplineGateRunner
{
    DisciplineOutcome Run(DisciplineContext context);
}

/// <summary>
/// Chạy toàn bộ gate kỷ luật và gộp kết quả.
/// </summary>
/// <remarks>
/// Khác <see cref="IEntryScorer"/> ở một điểm quan trọng: bộ này KHÔNG dừng sớm. Vòng chấm điểm
/// dừng ở veto đầu tiên để phiếu nêu đúng một lý do; còn ở đây phải chạy hết, vì trader cần
/// thấy TOÀN BỘ những gì đang chặn mình. Biết "hôm nay vừa chạm giới hạn lỗ vừa đủ hạn mức
/// lệnh" khác hẳn với chỉ biết một trong hai.
///
/// Quy tắc gộp:
/// <list type="bullet">
/// <item><c>StopForDay</c> thắng mọi thứ, rồi đến <c>BlockTrade</c>.</item>
/// <item><see cref="GateAggregate.SizeMultiplier"/> là TÍCH của các hệ số — hai gate cùng yêu
/// cầu giảm một nửa thì thành một phần tư, không phải một nửa.</item>
/// <item>Điểm phạt là TỔNG, và luôn ≤ 0.</item>
/// </list>
/// </remarks>
public sealed class DisciplineGateRunner : IDisciplineGateRunner
{
    private readonly IReadOnlyList<IDisciplineGate> _gates;

    public DisciplineGateRunner(IEnumerable<IDisciplineGate> gates) =>
        // Thứ tự tất định theo khoá, để dòng phiếu luôn xếp giống nhau bất kể DI trả về ra sao.
        _gates = gates.OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

    public DisciplineOutcome Run(DisciplineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var lines = new List<GateLine>(_gates.Count);
        var multiplier = 1.0m;
        var penalty = 0;

        GateResult? blocker = null;

        foreach (var gate in _gates)
        {
            var result = gate.Evaluate(context);
            lines.Add(new GateLine(gate.Key, result));

            penalty += Math.Min(0, result.ScorePenalty);

            // Kẹp ở đây chứ không tin gate: một hệ số > 1 lọt qua sẽ làm lệnh TO LÊN, tức
            // đúng điều mà cả tầng này tồn tại để ngăn.
            multiplier *= Math.Clamp(result.SizeMultiplier, 0m, 1m);

            if (result.Action is GateAction.BlockTrade or GateAction.StopForDay)
            {
                // Nặng hơn thì thay; ngang nhau thì giữ cái gặp trước để lý do ổn định.
                if (blocker is null || result.Action > blocker.Action) blocker = result;
            }
        }

        var aggregate = blocker is null
            ? new GateAggregate(multiplier, penalty, false, null, null)
            : new GateAggregate(0m, penalty, true, blocker.VetoReason, blocker.Reason);

        return new DisciplineOutcome(aggregate, lines);
    }
}
