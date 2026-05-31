using MMW.Domain.Enums;

namespace MMW.Application.Behavior;

/// <summary>
/// Một bộ phát hiện hành vi. Trả BehaviorSignal nếu phát hiện, null nếu không.
/// </summary>
public interface IBehaviorDetector
{
    FlagType Type { get; }

    BehaviorSignal? Detect(BehaviorContext ctx);
}
