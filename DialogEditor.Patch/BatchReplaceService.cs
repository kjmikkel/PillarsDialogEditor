using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class BatchReplaceService
{
    public static IReadOnlyList<BatchConversationResult> DryRun(
        BatchReplaceQuery               query,
        IReadOnlyList<ConversationFile> files,
        IGameDataProvider               provider)
    {
        var results    = new List<BatchConversationResult>();
        var comparison = query.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (var file in files)
        {
            var conversation = provider.LoadConversation(file);
            var snapshot     = ConversationSnapshotBuilder.Build(conversation);
            var matches      = new List<BatchFieldMatch>();

            foreach (var node in snapshot.Nodes)
                CollectMatches(node, query, comparison, matches);

            if (matches.Count > 0)
                results.Add(new BatchConversationResult(file, matches));
        }

        return results;
    }

    public static void Apply(
        IReadOnlyList<BatchConversationResult> results,
        IGameDataProvider                      provider)
    {
        foreach (var result in results)
        {
            // Re-load to pick up any changes since DryRun
            var conversation = provider.LoadConversation(result.File);
            var snapshot     = ConversationSnapshotBuilder.Build(conversation);

            // Rebuild the match set for this fresh load using the matches' FieldPaths as a guide —
            // simpler: re-derive the query from the result's own Before/After pairs per node.
            // Since Apply is always called immediately after DryRun in practice, we re-run the
            // replacement directly on the fresh snapshot using the same before→after pairs.
            var nodePatches = result.Matches
                .GroupBy(m => m.NodeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var newNodes = snapshot.Nodes.Select(node =>
            {
                if (!nodePatches.TryGetValue(node.NodeId, out var patches))
                    return node;

                return ApplyToNode(node, patches);
            }).ToList();

            provider.SaveConversation(result.File, new ConversationEditSnapshot(newNodes));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static void CollectMatches(
        NodeEditSnapshot      node,
        BatchReplaceQuery     query,
        StringComparison      comparison,
        List<BatchFieldMatch> matches)
    {
        var s = query.SearchText;
        var r = query.ReplaceText;

        if (query.InNodeText)
        {
            Check(node, "Default Text",  node.DefaultText,  s, r, comparison, matches);
            Check(node, "Female Text",   node.FemaleText,   s, r, comparison, matches);
        }

        if (query.InSpeakerGuids)
        {
            Check(node, "Speaker GUID",  node.SpeakerGuid,  s, r, comparison, matches);
            Check(node, "Listener GUID", node.ListenerGuid, s, r, comparison, matches);
        }

        if (query.InScriptParams)
        {
            for (var si = 0; si < node.Scripts.Count; si++)
            {
                var script = node.Scripts[si];
                for (var pi = 0; pi < script.Parameters.Count; pi++)
                {
                    var cat = script.Category.ToString();
                    Check(node, $"Script {cat}[{si}] Param {pi}",
                          script.Parameters[pi], s, r, comparison, matches);
                }
            }
        }

        if (query.InConditionParams)
        {
            var ci = 0;
            foreach (var leaf in node.Conditions.SelectMany(c => c.Leaves()).OfType<ConditionLeaf>())
            {
                for (var pi = 0; pi < leaf.Parameters.Count; pi++)
                    Check(node, $"Condition[{ci}] Param {pi}",
                          leaf.Parameters[pi], s, r, comparison, matches);
                ci++;
            }
        }

        if (query.InLinkChoiceText)
        {
            foreach (var link in node.Links)
                Check(node, "Link Choice Text",
                      link.QuestionNodeTextDisplay, s, r, comparison, matches);
        }
    }

    private static void Check(
        NodeEditSnapshot      node,
        string                fieldPath,
        string                value,
        string                search,
        string                replace,
        StringComparison      comparison,
        List<BatchFieldMatch> matches)
    {
        if (!value.Contains(search, comparison)) return;
        var after = StringReplace.ReplaceAll(value, search, replace, comparison);
        matches.Add(new BatchFieldMatch(node.NodeId, fieldPath, value, after));
    }

    private static NodeEditSnapshot ApplyToNode(
        NodeEditSnapshot         node,
        List<BatchFieldMatch>    patches)
    {
        // Index patches by FieldPath for O(1) lookup
        var byField = patches.ToDictionary(p => p.FieldPath);

        string Get(string fieldPath, string current)
            => byField.TryGetValue(fieldPath, out var m) ? m.After : current;

        var newScripts = node.Scripts.Select((script, si) =>
        {
            var cat = script.Category.ToString();
            var newParams = script.Parameters.Select((p, pi) =>
                Get($"Script {cat}[{si}] Param {pi}", p)).ToList();
            return newParams.SequenceEqual(script.Parameters)
                ? script
                : new ScriptCall(script.FullName, newParams, script.Category);
        }).ToList();

        var newConditions = ReplaceConditionParams(node.Conditions, byField);

        var newLinks = node.Links.Select(link =>
        {
            var qtd = Get("Link Choice Text", link.QuestionNodeTextDisplay);
            return qtd == link.QuestionNodeTextDisplay
                ? link
                : link with { QuestionNodeTextDisplay = qtd };
        }).ToList();

        return node with
        {
            DefaultText  = Get("Default Text",  node.DefaultText),
            FemaleText   = Get("Female Text",   node.FemaleText),
            SpeakerGuid  = Get("Speaker GUID",  node.SpeakerGuid),
            ListenerGuid = Get("Listener GUID", node.ListenerGuid),
            Scripts      = newScripts,
            Conditions   = newConditions,
            Links        = newLinks,
        };
    }

    private static IReadOnlyList<ConditionNode> ReplaceConditionParams(
        IReadOnlyList<ConditionNode>  conditions,
        Dictionary<string, BatchFieldMatch> byField)
    {
        // Re-index conditions by their leaf order to match FieldPaths used in DryRun
        var ci      = 0;
        var changed = false;
        var result  = new List<ConditionNode>();

        foreach (var node in conditions)
            result.Add(ReplaceInConditionNode(node, byField, ref ci, ref changed));

        return changed ? result : conditions;
    }

    private static ConditionNode ReplaceInConditionNode(
        ConditionNode                       node,
        Dictionary<string, BatchFieldMatch> byField,
        ref int                             ci,
        ref bool                            changed)
    {
        if (node is ConditionLeaf leaf)
        {
            var newParams = new List<string>(leaf.Parameters.Count);
            var modified  = false;
            for (var pi = 0; pi < leaf.Parameters.Count; pi++)
            {
                var key = $"Condition[{ci}] Param {pi}";
                if (byField.TryGetValue(key, out var m))
                { newParams.Add(m.After); modified = true; }
                else
                { newParams.Add(leaf.Parameters[pi]); }
            }
            ci++;
            if (!modified) return leaf;
            changed = true;
            return leaf with { Parameters = newParams };
        }

        if (node is ConditionBranch branch)
        {
            var newComponents = new List<ConditionNode>(branch.Components.Count);
            foreach (var c in branch.Components)
                newComponents.Add(ReplaceInConditionNode(c, byField, ref ci, ref changed));
            return changed ? branch with { Components = newComponents } : branch;
        }

        return node;
    }
}
