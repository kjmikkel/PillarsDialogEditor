namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// An address for an element. Every criterion supplied is ANDed. Nth is the only way
/// to accept an ambiguous match — the resolver otherwise errors rather than guessing.
/// </summary>
public record Selector(
    string? Name = null,
    string? ControlType = null,
    string? AutomationId = null,
    string? WithinPane = null,
    int? Nth = null)
{
    public bool IsEmpty => Name is null && ControlType is null && AutomationId is null;
}
