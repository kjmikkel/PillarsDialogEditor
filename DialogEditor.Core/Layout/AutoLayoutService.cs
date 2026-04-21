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

        foreach (var layer in byLayer)
        {
            var nodeIds = layer.ToList();
            var x = layer.Key * (NodeWidth + HorizontalGap);
            var startY = -(nodeIds.Count - 1) * (NodeHeight + VerticalGap) / 2.0;

            for (var i = 0; i < nodeIds.Count; i++)
            {
                var y = startY + i * (NodeHeight + VerticalGap);
                setLocation(nodeIds[i], x, y);
            }
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
