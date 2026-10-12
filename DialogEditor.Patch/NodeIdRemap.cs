using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

/// Re-keys stored translations after the canvas renumbered nodes (issue 172). The canvas
/// language's text moves with the node itself; the other languages exist only in the stored
/// patch, and FoldCanvasIntoProject drops entries whose node no longer exists.
public static class NodeIdRemap
{
    public static IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> Apply(
        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations,
        IReadOnlyList<(int From, int To)> remaps)
    {
        var net = Net(remaps);
        if (net.Count == 0) return translations;

        // Every target had no live node when it was renumbered onto, so anything stored under
        // it (a deleted node 0's text) is stale, whether or not this language has an entry for
        // the node that moved there.
        var targets = net.Values.ToHashSet();
        var result  = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
        foreach (var (lang, entries) in translations)
            result[lang] = [.. entries
                .Where(t => net.ContainsKey(t.NodeId) || !targets.Contains(t.NodeId))
                .Select(t => net.TryGetValue(t.NodeId, out var to) ? t with { NodeId = to } : t)];
        return result;
    }

    /// Collapses the log into where each original ID ends up, dropping round trips: a renumber
    /// followed by its Undo is no change, so a node 0 restored by undoing its deletion keeps
    /// its own entries.
    private static Dictionary<int, int> Net(IReadOnlyList<(int From, int To)> remaps)
    {
        var net = new Dictionary<int, int>();   // original ID -> current ID
        foreach (var (from, to) in remaps)
        {
            // The node now at `from` started at whichever original ID maps to it, if any.
            var origin = from;
            foreach (var (start, current) in net)
                if (current == from) { origin = start; break; }
            net[origin] = to;
        }
        foreach (var id in net.Where(kv => kv.Key == kv.Value).Select(kv => kv.Key).ToList())
            net.Remove(id);
        return net;
    }
}
