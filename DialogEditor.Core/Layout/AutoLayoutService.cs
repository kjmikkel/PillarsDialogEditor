using DialogEditor.Core.Models;

namespace DialogEditor.Core.Layout;

public static class AutoLayoutService
{
    /// <summary>Lays out with every card at its largest size. For callers without the text.</summary>
    public static void Apply(IReadOnlyList<ConversationNode> nodes, Action<int, double, double> setLocation)
        => Apply(nodes, _ => CardMetrics.MaxHeight, setLocation);

    /// <summary>
    /// Layered layout (issue 82): column pitch is the card width plus <see cref="CardMetrics.ColumnGap"/>.
    /// Within a column each card sits <see cref="CardMetrics.RowGap"/> below the previous card's
    /// bottom edge, using <paramref name="heightOf"/>, so tall and short cards neither overlap nor
    /// leave fixed-pitch holes. Callers pass <c>CardMetrics.EstimateHeight(text)</c>.
    /// A full card (CardMetrics.MaxHeight) is under the old 130 px pitch, so layouts saved
    /// before this change still don't overlap.
    /// </summary>
    public static void Apply(
        IReadOnlyList<ConversationNode> nodes,
        Func<int, double> heightOf,
        Action<int, double, double> setLocation)
    {
        var layers = AssignLayers(nodes);
        var byLayer = layers.GroupBy(kv => kv.Value, kv => kv.Key).OrderBy(g => g.Key).ToList();

        // Stable tiebreak for nodes that want the same row: original input order.
        var inputOrder = new Dictionary<int, int>();
        for (var i = 0; i < nodes.Count; i++) inputOrder[nodes[i].NodeId] = i;

        // Parents restricted to earlier layers, so their Y is already assigned when a
        // child is placed. Back-edges and same-layer links are ignored for positioning.
        var parents = nodes
            .SelectMany(n => n.Links
                .Where(l => layers.TryGetValue(l.ToNodeId, out var childLayer) && childLayer > layers[n.NodeId])
                .Select(l => (Child: l.ToNodeId, Parent: n.NodeId)))
            .GroupBy(p => p.Child)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Parent).ToList());

        var assignedY = new Dictionary<int, double>();

        foreach (var layer in byLayer)
        {
            var nodeIds = layer.ToList();

            if (layer.Key == 0)
            {
                // Roots have no parent to align to: stack them, centred on the origin.
                // As before, the first and last roots' tops sit symmetrically about 0 (a single
                // root lands at exactly 0).
                var total = nodeIds.Sum(heightOf) + CardMetrics.RowGap * (nodeIds.Count - 1);
                var y0 = -(total - heightOf(nodeIds[^1])) / 2.0;
                foreach (var id in nodeIds)
                {
                    assignedY[id] = y0;
                    y0 += heightOf(id) + CardMetrics.RowGap;
                }
                continue;
            }

            // Desired row = barycentre of already-placed parents (fallback to origin).
            double Desired(int id) =>
                parents.TryGetValue(id, out var ps) && ps.Count > 0 ? ps.Average(p => assignedY[p]) : 0.0;

            // Anchor the topmost node at its desired row, then push each next one down only as far
            // as needed to clear the previous card's bottom edge plus the row gap.
            var nextFree = double.NegativeInfinity;
            foreach (var id in nodeIds.OrderBy(Desired).ThenBy(id => inputOrder[id]))
            {
                var y = Math.Max(Desired(id), nextFree);
                assignedY[id] = y;
                nextFree = y + heightOf(id) + CardMetrics.RowGap;
            }
        }

        foreach (var layer in byLayer)
        {
            var x = layer.Key * (CardMetrics.Width + CardMetrics.ColumnGap);
            foreach (var id in layer) setLocation(id, x, assignedY[id]);
        }
    }

    private static Dictionary<int, int> AssignLayers(IReadOnlyList<ConversationNode> nodes)
    {
        var targeted = nodes
            .SelectMany(n => n.Links.Select(l => l.ToNodeId))
            .ToHashSet();

        var layers = new Dictionary<int, int>();
        var queue = new Queue<int>();

        foreach (var node in nodes.Where(n => !targeted.Contains(n.NodeId)))
        {
            layers[node.NodeId] = 0;
            queue.Enqueue(node.NodeId);
        }

        if (layers.Count == 0 && nodes.Count > 0)
        {
            layers[nodes[0].NodeId] = 0;
            queue.Enqueue(nodes[0].NodeId);
        }

        var nodeMap = nodes.ToDictionary(n => n.NodeId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!nodeMap.TryGetValue(current, out var node)) continue;

            foreach (var link in node.Links)
            {
                if (!layers.ContainsKey(link.ToNodeId))
                {
                    layers[link.ToNodeId] = layers[current] + 1;
                    queue.Enqueue(link.ToNodeId);
                }
            }
        }

        var maxLayer = layers.Count > 0 ? layers.Values.Max() : 0;
        foreach (var node in nodes.Where(n => !layers.ContainsKey(n.NodeId)))
            layers[node.NodeId] = ++maxLayer;

        return layers;
    }
}
