namespace DialogEditor.UiaMcp.Core;

/// <summary>The seam between addressing logic and live UI Automation.</summary>
public interface IUiaTree
{
    ElementInfo Root { get; }
    IReadOnlyList<ElementInfo> ChildrenOf(string id);
}
