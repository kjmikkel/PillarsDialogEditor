namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Pure keyboard-traversal logic for the conversation canvas (spec:
/// docs/superpowers/specs/2026-06-12-canvas-keyboard-editing-design.md).
///
/// Traversal is TOPOLOGICAL (follows links), not spatial: it matches both the
/// left-to-right auto-layout and the dialog structure writers think in, and it
/// stays correct after nodes are hand-rearranged. Where several candidates
/// exist (multiple children/parents), the one vertically nearest the current
/// node wins, so the keyboard "follows the eye"; ties keep link order (LINQ
/// OrderBy is stable). Self-loops are skipped — navigating to yourself is a
/// no-op, not a move.
/// </summary>
public static class CanvasNavigationService
{
    public static NodeViewModel? GetChild(
        NodeViewModel from,
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections) =>
        NearestByY(from, connections
            .Where(c => c.Source.GetNodeId() == from.NodeId)
            .Select(c => ById(nodes, c.Target.GetNodeId()))
            .OfType<NodeViewModel>()
            .Where(n => n.NodeId != from.NodeId));

    public static NodeViewModel? GetParent(
        NodeViewModel from,
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections) =>
        NearestByY(from, connections
            .Where(c => c.Target.GetNodeId() == from.NodeId)
            .Select(c => ById(nodes, c.Source.GetNodeId()))
            .OfType<NodeViewModel>()
            .Where(n => n.NodeId != from.NodeId));

    private static NodeViewModel? ById(IReadOnlyList<NodeViewModel> nodes, int id) =>
        nodes.FirstOrDefault(n => n.NodeId == id);

    private static NodeViewModel? NearestByY(NodeViewModel from, IEnumerable<NodeViewModel> candidates) =>
        candidates.OrderBy(n => Math.Abs(n.Location.Y - from.Location.Y)).FirstOrDefault();

    /// <summary>
    /// Siblings of a node = children of its primary parent (the same parent ←
    /// navigates to), in visual (Y) order, no wrap. Parentless nodes (roots and
    /// orphans) form a single sibling group so ↑/↓ can hop between disconnected
    /// islands without the mouse.
    /// </summary>
    public static NodeViewModel? GetSibling(
        NodeViewModel from,
        int offset,
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections)
    {
        var connList = connections as IReadOnlyList<ConnectionViewModel> ?? connections.ToList();
        var parent = GetParent(from, nodes, connList);

        var group = (parent is null
                ? nodes.Where(n => GetParent(n, nodes, connList) is null)
                : connList.Where(c => c.Source.GetNodeId() == parent.NodeId)
                          .Select(c => ById(nodes, c.Target.GetNodeId()))
                          .OfType<NodeViewModel>()
                          .Distinct())
            .OrderBy(n => n.Location.Y)
            .ToList();

        var index = group.IndexOf(from);
        if (index < 0) return null;
        var target = index + offset;
        return target >= 0 && target < group.Count ? group[target] : null;
    }
}
