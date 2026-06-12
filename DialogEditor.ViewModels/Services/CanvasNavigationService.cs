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
}
