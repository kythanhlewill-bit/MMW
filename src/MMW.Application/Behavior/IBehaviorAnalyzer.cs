namespace MMW.Application.Behavior;

public interface IBehaviorAnalyzer
{
    IReadOnlyList<BehaviorSignal> Analyze(BehaviorContext ctx);
}

/// <summary>
/// Chạy toàn bộ IBehaviorDetector đã đăng ký và gom tín hiệu.
/// Thêm hành vi mới = thêm 1 class IBehaviorDetector + đăng ký DI.
/// </summary>
public class BehaviorAnalyzer : IBehaviorAnalyzer
{
    private readonly IEnumerable<IBehaviorDetector> _detectors;

    public BehaviorAnalyzer(IEnumerable<IBehaviorDetector> detectors) => _detectors = detectors;

    public IReadOnlyList<BehaviorSignal> Analyze(BehaviorContext ctx)
    {
        var signals = new List<BehaviorSignal>();
        foreach (var detector in _detectors)
        {
            var s = detector.Detect(ctx);
            if (s is not null)
                signals.Add(s);
        }
        return signals;
    }
}
