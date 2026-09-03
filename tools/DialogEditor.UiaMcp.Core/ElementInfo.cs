namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// A UIA element flattened to plain data. Deliberately free of any UI Automation type
/// so this assembly stays net8.0 and unit-testable from DialogEditor.Tests.
/// <paramref name="Id"/> is a tree-local identity assigned by the IUiaTree implementation.
/// </summary>
public record ElementInfo(
    string Id,
    string Name,
    string ControlType,
    string AutomationId,
    string ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsFocusable,
    IReadOnlyList<string> Patterns);
