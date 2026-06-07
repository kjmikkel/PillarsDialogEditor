using DialogEditor.Core.Models;

namespace DialogEditor.Core.Layout;

public static class AutoLayoutService
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 110;
    private const double HorizontalGap = 200;
    private const double VerticalGap = 20;

    public static void Apply(
        IReadOnlyList<ConversationNode> nodes,
        Action<int, double, double> setLocation)
    {
        var layers = AssignLayers(nodes);
        var byLayer = layers
            .GroupBy(kv => kv.Value, kv => kv.Key)
            .OrderBy(g => g.Key)
            .ToList();

        const double pitch = NodeHeight + VerticalGap;

        // Stable tiebreak for nodes that want the same row: original input order.
        var inputOrder = new Dictionary<int, int>();
        for (var i = 0; i < nodes.Count; i++)
            inputOrder[nodes[i].NodeId] = i;

        // Parents restricted to earlier layers, so their Y is already assigned when a
        // child is placed. Back-edges and same-layer links are ignored for positioning.
        var parents = nodes
            .SelectMany(n => n.Links
                .Where(l => layers.TryGetValue(l.ToNodeId, out var childLayer)
                            && childLayer > layers[n.NodeId])
                .Select(l => (Child: l.ToNodeId, Parent: n.NodeId)))
            .GroupBy(p => p.Child)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Parent).ToList());

        var assignedY = new Dictionary<int, double>();

        foreach (var layer in byLayer)
        {
            var nodeIds = layer.ToList();

            if (layer.Key == 0)
            {
                // Roots have no parent to align to — keep them centred on the origin.
                var startY = -(nodeIds.Count - 1) * pitch / 2.0;
                for (var i = 0; i < nodeIds.Count; i++)
                    assignedY[nodeIds[i]] = startY + i * pitch;
                continue;
            }

            // Desired row = barycentre of already-placed parents (fallback to origin).
            double Desired(int id) =>
                parents.TryGetValue(id, out var ps) && ps.Count > 0
                    ? ps.Average(p => assignedY[p])
                    : 0.0;

            // Anchor the topmost node at its desired row; push the rest down only as far
            // as needed to keep the row pitch — so a node is never bumped without a reason.
            var prev = double.NegativeInfinity;
            foreach (var id in nodeIds.OrderBy(Desired).ThenBy(id => inputOrder[id]))
            {
                var y = Math.Max(Desired(id), prev + pitch);
                assignedY[id] = y;
                prev = y;
            }
        }

        foreach (var layer in byLayer)
        {
            var x = layer.Key * (NodeWidth + HorizontalGap);
            foreach (var id in layer)
                setLocation(id, x, assignedY[id]);
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
