using DialogEditor.Core.Models;

namespace DialogEditor.Patch.Diff;

/// Cherry-picks individual nodes from a source project's patches into a target
/// project's patches. For each selected node the target's contribution is made
/// identical to the source's (which may be "nothing" — i.e. revert to base).
/// Pure; never mutates its inputs.
public static class NodeApplyBuilder
{
    public static DialogProject Apply(
        DialogProject target,
        DialogProject source,
        IReadOnlyList<NodeSelection> selected)
    {
        if (selected.Count == 0) return target;

        var byConv = selected
            .GroupBy(s => s.ConversationName)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.Select(s => s.NodeId).ToHashSet());

        var patches = new Dictionary<string, ConversationPatch>(target.Patches);
        foreach (var (conv, ids) in byConv)
        {
            var targetPatch = target.Patches.GetValueOrDefault(conv) ?? Empty(conv);
            var sourcePatch = source.Patches.GetValueOrDefault(conv);
            patches[conv] = ApplyNodes(targetPatch, sourcePatch, ids);
        }

        return target with { Patches = patches };
    }

    private static ConversationPatch Empty(string conv) =>
        new(conv, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static ConversationPatch ApplyNodes(
        ConversationPatch target, ConversationPatch? source, IReadOnlySet<int> ids)
    {
        var added    = target.AddedNodes.Where(n => !ids.Contains(n.NodeId)).ToList();
        var modified = target.ModifiedNodes.Where(m => !ids.Contains(m.NodeId)).ToList();
        var deleted  = target.DeletedNodeIds.Where(d => !ids.Contains(d)).ToList();
        var translations = target.Translations.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(t => !ids.Contains(t.NodeId)).ToList());

        if (source is not null)
        {
            added.AddRange(source.AddedNodes.Where(n => ids.Contains(n.NodeId)));
            modified.AddRange(source.ModifiedNodes.Where(m => ids.Contains(m.NodeId)));
            deleted.AddRange(source.DeletedNodeIds.Where(ids.Contains));
            foreach (var (lang, list) in source.Translations)
            {
                var picked = list.Where(t => ids.Contains(t.NodeId)).ToList();
                if (picked.Count == 0) continue;
                if (!translations.TryGetValue(lang, out var existing))
                    translations[lang] = existing = new List<NodeTranslation>();
                existing.AddRange(picked);
            }
        }

        var cleaned = translations
            .Where(kv => kv.Value.Count > 0)
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyList<NodeTranslation>)kv.Value);

        return target with
        {
            AddedNodes     = added,
            ModifiedNodes  = modified,
            DeletedNodeIds = deleted,
            Translations   = cleaned,
        };
    }
}
