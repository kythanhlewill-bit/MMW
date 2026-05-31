using MMW.Domain.Enums;

namespace MMW.Application.RuleEngine;

/// <summary>
/// Mô tả một vi phạm rule (chưa gắn ID) — service sẽ chuyển thành entity Flag.
/// </summary>
public sealed record RuleViolation(
    FlagType Type,
    FlagSeverity Severity,
    string Message,
    string? DetailJson = null);
