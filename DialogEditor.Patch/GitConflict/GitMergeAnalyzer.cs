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
        // ── Field edits ──────────────────────────────────────────────────
        var mineFields  = BuildFieldMap(mine);
        var theirFields = BuildFieldMap(theirs);

        foreach (var (key, mineValue) in mineFields)
        {
            if (theirFields.TryGetValue(key, out var theirValue)
                && !string.Equals(mineValue, theirValue, StringComparison.Ordinal))
            {
                conflicts.Add(new MergeConflict(
                    MergeConflictKind.FieldEdit, conv, key.NodeId, key.Field, mineValue, theirValue));
            }
        }

        // ── Delete-vs-edit ───────────────────────────────────────────────
        var mineTouched  = TouchedNodeIds(mine);
        var theirTouched = TouchedNodeIds(theirs);

        foreach (var nodeId in mine.DeletedNodeIds)
            if (theirTouched.Contains(nodeId))
                conflicts.Add(new MergeConflict(
                    MergeConflictKind.DeleteVsEdit, conv, nodeId, null,
                    DeletedMarker, DescribeTouched(theirs, nodeId)));

        foreach (var nodeId in theirs.DeletedNodeIds)
            if (mineTouched.Contains(nodeId))
                conflicts.Add(new MergeConflict(
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
                conflicts.Add(new MergeConflict(
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
                conflicts.Add(new MergeConflict(
                    MergeConflictKind.TranslationEdit, conv, key.NodeId, key.Lang,
                    DisplayText(mineT, theirT), DisplayText(theirT, mineT)));
        }
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
