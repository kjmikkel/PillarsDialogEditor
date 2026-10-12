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
        if (remaps.Count == 0) return translations;

        var result = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
        foreach (var (lang, entries) in translations)
        {
            var list = entries.ToList();
            foreach (var (from, to) in remaps)
            {
                // The target ID had no node before the renumber, so anything stored under it
                // is stale; drop it rather than keep two entries for one node. When nothing
                // moves onto the target, nothing replaced it, so it stays.
                if (!list.Any(t => t.NodeId == from)) continue;
                list.RemoveAll(t => t.NodeId == to);
                list = [.. list.Select(t => t.NodeId == from ? t with { NodeId = to } : t)];
            }
            result[lang] = list;
        }
        return result;
    }
}
