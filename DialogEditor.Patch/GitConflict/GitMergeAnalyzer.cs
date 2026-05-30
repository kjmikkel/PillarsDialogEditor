using System.Text.Json;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch.GitConflict;

/// Value-aware diff of two reconstructed project sides ("mine" and "theirs").
/// Emits a conflict only where the two sides actually disagree — because the
/// reconstructed sides are byte-identical outside git's conflict hunks, the
/// differences found here are exactly the conflicting regions.
///
/// Models its traversal on <see cref="ConflictDetector"/> but compares values
/// rather than merely flagging fields touched by both sides.
public static class GitMergeAnalyzer
{
    private const string DeletedMarker = MergeConflict.DeletedMarker;

    public static List<MergeConflict> Analyze(DialogProject mine, DialogProject theirs)
    {
        var conflicts = new List<MergeConflict>();

        foreach (var (convName, minePatch) in mine.Patches)
        {
            // A conversation present in only one side is not a conflict — it is
            // carried through unchanged by the merge.
            if (!theirs.Patches.TryGetValue(convName, out var theirPatch))
                continue;

            AnalyzeConversation(convName, minePatch, theirPatch, conflicts);
        }

        return conflicts;
    }

    private static void AnalyzeConversation(
        string conv, ConversationPatch mine, ConversationPatch theirs, List<MergeConflict> conflicts)
    {
        var granular = new List<MergeConflict>();

        // ── Field edits ──────────────────────────────────────────────────
        var mineFields  = BuildFieldMap(mine);
        var theirFields = BuildFieldMap(theirs);

        foreach (var (key, mineValue) in mineFields)
        {
            if (theirFields.TryGetValue(key, out var theirValue)
                && !string.Equals(mineValue, theirValue, StringComparison.Ordinal))
            {
                granular.Add(new MergeConflict(
                    MergeConflictKind.FieldEdit, conv, key.NodeId, key.Field, mineValue, theirValue));
            }
        }

        // ── Delete-vs-edit ───────────────────────────────────────────────
        var mineTouched  = TouchedNodeIds(mine);
        var theirTouched = TouchedNodeIds(theirs);

        foreach (var nodeId in mine.DeletedNodeIds)
            if (theirTouched.Contains(nodeId))
                granular.Add(new MergeConflict(
                    MergeConflictKind.DeleteVsEdit, conv, nodeId, null,
                    DeletedMarker, DescribeTouched(theirs, nodeId)));

        foreach (var nodeId in theirs.DeletedNodeIds)
            if (mineTouched.Contains(nodeId))
                granular.Add(new MergeConflict(
                    MergeConflictKind.DeleteVsEdit, conv, nodeId, null,
                    DescribeTouched(mine, nodeId), DeletedMarker));

        // ── Add/add ──────────────────────────────────────────────────────
        // Last-wins index — tolerant of a malformed reconstruction with
        // duplicate NodeIds (a plain ToDictionary would throw).
        var theirAdded = new Dictionary<int, Core.Editing.NodeEditSnapshot>();
        foreach (var node in theirs.AddedNodes)
            theirAdded[node.NodeId] = node;

        foreach (var mineNode in mine.AddedNodes)
        {
            if (!theirAdded.TryGetValue(mineNode.NodeId, out var theirNode))
                continue;

            var mineJson  = JsonSerializer.Serialize(mineNode);
            var theirJson = JsonSerializer.Serialize(theirNode);
            if (!string.Equals(mineJson, theirJson, StringComparison.Ordinal))
                granular.Add(new MergeConflict(
                    MergeConflictKind.NodeAddAdd, conv, mineNode.NodeId, null, mineJson, theirJson));
        }

        // ── Translation (text) edits ─────────────────────────────────────
        // Node text lives in Translations[lang], not FieldChanges (see DiffEngine),
        // so text conflicts surface only here.
        var mineTr  = BuildTranslationMap(mine);
        var theirTr = BuildTranslationMap(theirs);

        foreach (var (key, mineT) in mineTr)
        {
            if (theirTr.TryGetValue(key, out var theirT) && !mineT.Equals(theirT))
                granular.Add(new MergeConflict(
                    MergeConflictKind.TranslationEdit, conv, key.NodeId, key.Lang,
                    DisplayText(mineT, theirT), DisplayText(theirT, mineT)));
        }

        // ── Conversation-level fallback ──────────────────────────────────
        // If theirs holds content the per-node merge can't preserve — an addition,
        // deletion, field, or translation on a node not wholly covered by a
        // delete-vs-edit / add-add — the granular merge would silently drop it.
        // Replace the granular conflicts with one whole-conversation choice.
        var wholeCovered = granular
            .Where(c => c.Kind is MergeConflictKind.DeleteVsEdit or MergeConflictKind.NodeAddAdd)
            .Select(c => c.NodeId)
            .ToHashSet();

        if (HasUncoveredTheirsContent(mine, theirs, mineFields, theirFields, mineTr, theirTr, wholeCovered))
            conflicts.Add(new MergeConflict(
                MergeConflictKind.ConversationLevel, conv, -1, null,
                SummarizePatch(mine), SummarizePatch(theirs)));
        else
            conflicts.AddRange(granular);
    }

    // True when theirs has any added node, deletion, modified field, or translation
    // that mine lacks and that no whole-node conflict (delete-vs-edit / add-add) covers
    // — i.e. content the granular merge (mine base + per-node overlays) would lose.
    private static bool HasUncoveredTheirsContent(
        ConversationPatch mine, ConversationPatch theirs,
        Dictionary<(int NodeId, string Field), string> mineFields,
        Dictionary<(int NodeId, string Field), string> theirFields,
        Dictionary<(int NodeId, string Lang), NodeTranslation> mineTr,
        Dictionary<(int NodeId, string Lang), NodeTranslation> theirTr,
        HashSet<int> wholeCovered)
    {
        var mineAdded   = mine.AddedNodes.Select(n => n.NodeId).ToHashSet();
        var mineDeleted = mine.DeletedNodeIds.ToHashSet();

        foreach (var node in theirs.AddedNodes)
            if (!mineAdded.Contains(node.NodeId) && !wholeCovered.Contains(node.NodeId))
                return true;

        foreach (var id in theirs.DeletedNodeIds)
            if (!mineDeleted.Contains(id) && !wholeCovered.Contains(id))
                return true;

        foreach (var key in theirFields.Keys)
            if (!wholeCovered.Contains(key.NodeId) && !mineFields.ContainsKey(key))
                return true;

        foreach (var key in theirTr.Keys)
            if (!wholeCovered.Contains(key.NodeId) && !mineTr.ContainsKey(key))
                return true;

        return false;
    }

    // Compact per-side summary shown for a whole-conversation conflict, e.g. "+1 ~2 -0 (3 text)".
    private static string SummarizePatch(ConversationPatch p)
    {
        var text = p.Translations.Sum(kv => kv.Value.Count);
        return $"+{p.AddedNodes.Count} ~{p.ModifiedNodes.Count} -{p.DeletedNodeIds.Count} ({text} text)";
    }

    private static Dictionary<(int NodeId, string Lang), NodeTranslation> BuildTranslationMap(ConversationPatch patch)
    {
        var map = new Dictionary<(int, string), NodeTranslation>();
        foreach (var (lang, list) in patch.Translations)
            foreach (var t in list)
                map[(t.NodeId, lang)] = t;   // last-wins
        return map;
    }

    // Show the field that actually differs: DefaultText when it differs, else the
    // FemaleText (so a female-only difference is still visible in the dialog).
    private static string DisplayText(NodeTranslation self, NodeTranslation other)
        => self.DefaultText != other.DefaultText ? self.DefaultText : self.FemaleText;

    // (nodeId, field) -> comparable value. FieldChanges contribute their `To`
    // value; the replace-all Conditions/Scripts lists are JSON-encoded so they
    // can be compared as a single pseudo-field (mirrors ConflictDetector).
    private static Dictionary<(int NodeId, string Field), string> BuildFieldMap(ConversationPatch patch)
    {
        var map = new Dictionary<(int, string), string>();
        foreach (var mod in patch.ModifiedNodes)
        {
            foreach (var (field, change) in mod.FieldChanges)
                map[(mod.NodeId, field)] = change.To;

            if (mod.UpdatedConditions is not null)
                map[(mod.NodeId, "Conditions")] = JsonSerializer.Serialize(mod.UpdatedConditions);
            if (mod.UpdatedScripts is not null)
                map[(mod.NodeId, "Scripts")] = JsonSerializer.Serialize(mod.UpdatedScripts);
        }
        return map;
    }

    private static HashSet<int> TouchedNodeIds(ConversationPatch patch)
    {
        var set = patch.ModifiedNodes.Select(m => m.NodeId).ToHashSet();
        foreach (var node in patch.AddedNodes)
            set.Add(node.NodeId);
        return set;
    }

    // A short display string for the side that kept the node (vs the deleting side).
    // Field names rather than raw JSON — readable in the resolution dialog.
    private static string DescribeTouched(ConversationPatch patch, int nodeId)
    {
        if (patch.AddedNodes.Any(n => n.NodeId == nodeId))
            return "(added)";

        var mod = patch.ModifiedNodes.FirstOrDefault(m => m.NodeId == nodeId);
        if (mod is not null)
        {
            var fields = mod.FieldChanges.Keys.ToList();
            if (mod.UpdatedConditions is not null) fields.Add("Conditions");
            if (mod.UpdatedScripts is not null)    fields.Add("Scripts");
            return fields.Count > 0 ? string.Join(", ", fields) : "(modified)";
        }

        return "(modified)";
    }
}
