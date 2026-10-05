using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

/// Find and replace across many conversations at once. It edits the *project*, never the
/// game folder (issue 124): each conversation is searched as the project sees it (vanilla plus
/// its patch), and the result is diffed back against vanilla into the project's patch, just
/// as Ctrl+S folds the canvas. The game files only change through Test Patch (F5), which
/// Restore (F6) and the patcher can undo; a direct write here could be undone by neither,
/// and it would never have become part of the mod.
public static class BatchReplaceService
{
    public static IReadOnlyList<BatchConversationResult> DryRun(
        BatchReplaceQuery               query,
        IReadOnlyList<ConversationFile> files,
        IGameDataProvider               provider,
        DialogProject                   project)
    {
        var results    = new List<BatchConversationResult>();
        var comparison = query.CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (var file in files)
        {
            var snapshot = Load(file, provider, project).Effective;
            var matches  = new List<BatchFieldMatch>();

            foreach (var node in snapshot.Nodes)
                CollectMatches(node, query, comparison, matches);

            if (matches.Count > 0)
                results.Add(new BatchConversationResult(file, matches));
        }

        return results;
    }

    /// Returns <paramref name="project"/> with each result's replacements folded into its
    /// conversation's patch. Writes nothing to disk.
    public static DialogProject Apply(
        IReadOnlyList<BatchConversationResult> results,
        IGameDataProvider                      provider,
        DialogProject                          project)
    {
        foreach (var result in results)
        {
            // Re-load to pick up any changes since DryRun, then replay the DryRun's
            // before→after pairs onto the fresh snapshot by field identity.
            var (vanilla, effective) = Load(result.File, provider, project);

            var nodePatches = result.Matches
                .GroupBy(m => m.NodeId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var newNodes = effective.Nodes.Select(node =>
            {
                if (!nodePatches.TryGetValue(node.NodeId, out var patches))
                    return node;

                return ApplyToNode(node, patches);
            }).ToList();

            // Diffing against vanilla re-states the project's earlier edits along with the
            // replacements. Translator comments are language-neutral and untouched by a
            // batch edit, so they come over as they were.
            var prior = project.Patches.GetValueOrDefault(result.File.Name);
            var patch = DiffEngine.Diff(result.File.Name, vanilla,
                                        new ConversationEditSnapshot(newNodes), provider.Language)
                .CarryingTranslationsFrom(prior, provider.Language);
            if (prior is not null)
                patch = patch with { NodeComments = prior.NodeComments };

            project = project.WithPatch(patch);
        }

        return project;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// A conversation as vanilla and as the project sees it: the patch replayed and the
    /// provider language's text laid over the nodes — what the canvas shows when it is
    /// opened (MainWindowViewModel.LoadConversationFile).
    private static (ConversationEditSnapshot Vanilla, ConversationEditSnapshot Effective) Load(
        ConversationFile  file,
        IGameDataProvider provider,
        DialogProject     project)
    {
        var vanilla = ConversationSnapshotBuilder.Build(provider.LoadConversation(file));
        if (!project.Patches.TryGetValue(file.Name, out var patch))
            return (vanilla, vanilla);

        // Forced like the canvas: a game update can move a field the patch also changes.
        // The project's edit still wins; only F5 keeps the strict conflict check, because
        // only F5 writes the game.
        var patched = PatchApplier.Apply(vanilla, patch, ignoreConflicts: true);
        var text    = patch.Translations.GetValueOrDefault(provider.Language);
        if (text is null)
            return (vanilla, patched);

        var byId = text.ToDictionary(t => t.NodeId);
        return (vanilla, new ConversationEditSnapshot(patched.Nodes.Select(n =>
            byId.TryGetValue(n.NodeId, out var t)
                ? n with { DefaultText = t.DefaultText, FemaleText = t.FemaleText }
                : n).ToList()));
    }

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
            Check(node, new BatchField(BatchFieldKind.DefaultText), node.DefaultText, s, r, comparison, matches);
            Check(node, new BatchField(BatchFieldKind.FemaleText),  node.FemaleText,  s, r, comparison, matches);
        }

        if (query.InSpeakerGuids)
        {
            Check(node, new BatchField(BatchFieldKind.SpeakerGuid),  node.SpeakerGuid,  s, r, comparison, matches);
            Check(node, new BatchField(BatchFieldKind.ListenerGuid), node.ListenerGuid, s, r, comparison, matches);
        }

        if (query.InScriptParams)
        {
            for (var si = 0; si < node.Scripts.Count; si++)
            {
                var script = node.Scripts[si];
                for (var pi = 0; pi < script.Parameters.Count; pi++)
                    Check(node, new BatchField(BatchFieldKind.ScriptParam, script.Category, si, pi),
                          script.Parameters[pi], s, r, comparison, matches);
            }
        }

        if (query.InConditionParams)
        {
            var ci = 0;
            foreach (var leaf in node.Conditions.SelectMany(c => c.Leaves()).OfType<ConditionLeaf>())
            {
                for (var pi = 0; pi < leaf.Parameters.Count; pi++)
                    Check(node, new BatchField(BatchFieldKind.ConditionParam, Index: ci, ParamIndex: pi),
                          leaf.Parameters[pi], s, r, comparison, matches);
                ci++;
            }
        }
    }

    private static void Check(
        NodeEditSnapshot      node,
        BatchField            field,
        string                value,
        string                search,
        string                replace,
        StringComparison      comparison,
        List<BatchFieldMatch> matches)
    {
        if (!value.Contains(search, comparison)) return;
        var after = StringReplace.ReplaceAll(value, search, replace, comparison);
        matches.Add(new BatchFieldMatch(node.NodeId, field, value, after));
    }

    private static NodeEditSnapshot ApplyToNode(
        NodeEditSnapshot         node,
        List<BatchFieldMatch>    patches)
    {
        // Index patches by field identity for O(1) lookup. BatchField is a record, so
        // value equality pairs a fresh snapshot's fields to the DryRun matches without
        // any string round-trip — see the note on BatchField.
        var byField = patches.ToDictionary(p => p.Field);

        string Get(BatchField field, string current)
            => byField.TryGetValue(field, out var m) ? m.After : current;

        var newScripts = node.Scripts.Select((script, si) =>
        {
            var newParams = script.Parameters.Select((p, pi) =>
                Get(new BatchField(BatchFieldKind.ScriptParam, script.Category, si, pi), p)).ToList();
            return newParams.SequenceEqual(script.Parameters)
                ? script
                : new ScriptCall(script.FullName, newParams, script.Category);
        }).ToList();

        var newConditions = ReplaceConditionParams(node.Conditions, byField);

        return node with
        {
            DefaultText  = Get(new BatchField(BatchFieldKind.DefaultText),  node.DefaultText),
            FemaleText   = Get(new BatchField(BatchFieldKind.FemaleText),   node.FemaleText),
            SpeakerGuid  = Get(new BatchField(BatchFieldKind.SpeakerGuid),  node.SpeakerGuid),
            ListenerGuid = Get(new BatchField(BatchFieldKind.ListenerGuid), node.ListenerGuid),
            Scripts      = newScripts,
            Conditions   = newConditions,
            // Links are intentionally left untouched: the only link field batch
            // replace ever reached was QuestionNodeTextDisplay, an enum that
            // must never be rewritten by substring replacement (#24).
        };
    }

    private static IReadOnlyList<ConditionNode> ReplaceConditionParams(
        IReadOnlyList<ConditionNode>  conditions,
        Dictionary<BatchField, BatchFieldMatch> byField)
    {
        // Re-index conditions by their leaf order to match the identities used in DryRun
        var ci      = 0;
        var changed = false;
        var result  = new List<ConditionNode>();

        foreach (var node in conditions)
            result.Add(ReplaceInConditionNode(node, byField, ref ci, ref changed));

        return changed ? result : conditions;
    }

    private static ConditionNode ReplaceInConditionNode(
        ConditionNode                           node,
        Dictionary<BatchField, BatchFieldMatch> byField,
        ref int                             ci,
        ref bool                            changed)
    {
        if (node is ConditionLeaf leaf)
        {
            var newParams = new List<string>(leaf.Parameters.Count);
            var modified  = false;
            for (var pi = 0; pi < leaf.Parameters.Count; pi++)
            {
                var key = new BatchField(BatchFieldKind.ConditionParam, Index: ci, ParamIndex: pi);
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
