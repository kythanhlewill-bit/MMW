using MMW.Domain.Enums;

namespace MMW.Application.Behavior;

/// <summary>
/// Một tín hiệu hành vi phát hiện được (chưa gắn ID) — service chuyển thành Flag (Category=Behavior).
/// </summary>
public sealed record BehaviorSignal(
    FlagType Type,
    FlagSeverity Severity,
    string Message,
    string? DetailJson = null);
