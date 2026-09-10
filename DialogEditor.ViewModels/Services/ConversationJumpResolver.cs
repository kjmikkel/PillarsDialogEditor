using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Resolves StartConversation handoffs into a pure <see cref="MultiConversationGraph"/> for
/// <see cref="PathStatsService"/>.
///
/// Owns ALL the IO, patch application and GUID resolution, because DialogEditor.Core has no
/// project references and PathStatsService must stay pure. Mirrors ProjectFindService's walk:
/// the open conversation uses its live snapshot (unsaved edits included); every other is
/// vanilla + patch; an unreadable conversation is warned and reported, never thrown.
///
/// Boundary: only conversations this project patches are followed. A handoff to anything else
/// becomes an UnfollowedJump, so content never silently disappears off the end of a report.
///
/// Spec: docs/superpowers/specs/2026-09-10-cross-conversation-path-stats-design.md
/// </summary>
public static class ConversationJumpResolver
{
    /// <param name="conversationNamesById">
    /// Conversation GUID → name. Supplied by the caller and cached at game-folder-open time:
    /// its source, IGameDataProvider.LoadGameDataNames(), parses every bundle on disk and is
    /// documented as called once per folder open, so it must not run per analysis.
    /// GameDataNameService is unsuitable — it stores NamedEntry(DisplayName, StoredValue) with
    /// DisplayName composed as "{name} — {id}", so recovering the name would mean splitting on
    /// " — ", which breaks on any filename containing an em-dash. Empty for PoE1, whose
    /// StartConversation carries the name directly.
    /// </param>
    public static MultiConversationGraph Resolve(
        DialogProject project,
        IGameDataProvider provider,
        string primaryLanguage,
        string openConversationName,
        ConversationEditSnapshot openSnapshot,
        IReadOnlyDictionary<string, string> conversationNamesById,
        ScriptCatalogue? catalogue = null)
    {
        var cat        = catalogue ?? ScriptCatalogue.Instance;
        var loaded     = new Dictionary<string, ConversationEditSnapshot>
                             { [openConversationName] = openSnapshot };
        var jumps      = new List<JumpEdge>();
        var unfollowed = new List<UnfollowedJump>();
        var queue      = new Queue<string>();
        queue.Enqueue(openConversationName);

        // Worklist from the open conversation, so only conversations actually reached by a
        // handoff are loaded — not every patched conversation. The `loaded` set doubles as the
        // visited set, which is what terminates cyclic handoffs.
        while (queue.Count > 0)
        {
            var convName = queue.Dequeue();
            foreach (var node in loaded[convName].Nodes)
            foreach (var script in node.Scripts)
            {
                if (!ConversationJumpVerbs.IsJump(script.DisplayName)) continue;

                var from   = new NodeRef(convName, node.NodeId);
                var target = ResolveTarget(script, cat, conversationNamesById);
                if (target is null)
                {
                    // Empty label, not a localised placeholder: the resolver stays free of
                    // Loc's startup-configured global state (this suite already runs serially
                    // because of Loc/AppSettings statics). The ViewModel substitutes
                    // PathStats_UnknownTarget when it renders an Unresolved row.
                    unfollowed.Add(new UnfollowedJump(
                        from, string.Empty, UnfollowedReason.Unresolved));
                    continue;
                }
                var (name, entryNode) = target.Value;

                if (!project.Patches.ContainsKey(name))
                {
                    unfollowed.Add(new UnfollowedJump(from, name, UnfollowedReason.NotPatched));
                    continue;
                }

                if (!loaded.ContainsKey(name))
                {
                    var snap = TryLoad(project, provider, primaryLanguage, name);
                    if (snap is null)
                    {
                        unfollowed.Add(new UnfollowedJump(from, name, UnfollowedReason.LoadFailed));
                        continue;
                    }
                    loaded[name] = snap;
                    queue.Enqueue(name);
                }
                jumps.Add(new JumpEdge(from, new NodeRef(name, entryNode)));
            }
        }

        return new MultiConversationGraph(openConversationName, loaded, jumps, unfollowed);
    }

    /// <summary>
    /// Locates the conversation and entry-node arguments by CATALOGUE METADATA, never by index,
    /// because the two games declare different signatures.
    ///
    /// Resolution then branches on the argument's CLR type taken from the reflection signature —
    /// PoE2's "Void StartConversation(Guid, Guid, Int32)" carries a GUID needing the map, PoE1's
    /// "Void StartConversation(Guid, String, Int32)" carries the name itself. Deliberately NOT
    /// the catalogue parameter's own Type field: that reads "GameData" for BOTH games, so it
    /// cannot tell them apart.
    /// </summary>
    private static (string Name, int EntryNode)? ResolveTarget(
        ScriptCall script, ScriptCatalogue catalogue,
        IReadOnlyDictionary<string, string> namesById)
    {
        var entry = catalogue.FindByFullName(script.FullName);
        if (entry is null) return null;

        var convIndex = -1;
        var nodeIndex = -1;
        for (var i = 0; i < entry.Parameters.Count; i++)
        {
            var p = entry.Parameters[i];
            if (string.Equals(p.LookupKind, "Conversation", StringComparison.Ordinal))
                convIndex = i;
            else if (string.Equals(p.Name, "Conversation Node ID", StringComparison.Ordinal))
                nodeIndex = i;
        }
        if (convIndex < 0 || convIndex >= script.Parameters.Count) return null;

        var raw       = script.Parameters[convIndex];
        var clrTypes  = SignatureTypes(entry.ReflectionFullName);
        var isGuidArg = convIndex < clrTypes.Count &&
                        string.Equals(clrTypes[convIndex], "Guid", StringComparison.Ordinal);

        var name = isGuidArg ? namesById.GetValueOrDefault(raw) : raw;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var entryNode = 0;
        if (nodeIndex >= 0 && nodeIndex < script.Parameters.Count)
            int.TryParse(script.Parameters[nodeIndex], out entryNode);

        return (name, entryNode);
    }

    /// The parenthesised CLR parameter types of a reflection signature, e.g.
    /// "Void StartConversation(Guid, String, Int32)" -> ["Guid", "String", "Int32"].
    private static IReadOnlyList<string> SignatureTypes(string fullName)
    {
        var open = fullName.IndexOf('(');
        var close = fullName.LastIndexOf(')');
        if (open < 0 || close <= open + 1) return [];
        return fullName[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ConversationEditSnapshot? TryLoad(
        DialogProject project, IGameDataProvider provider,
        string primaryLanguage, string convName)
    {
        try
        {
            var patch    = project.Patches[convName];
            var file     = provider.FindConversation(convName);
            var baseSnap = file is not null
                ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                : new ConversationEditSnapshot([]);
            var applied  = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
            return WithTextRestored(applied, patch, primaryLanguage);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Path stats: could not load '{convName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// NodeEditSnapshot.DefaultText/FemaleText are [JsonIgnore], so nodes the writer ADDED come
    /// back from a patch empty. Without this every patch-loaded conversation would count zero
    /// words — wrong numbers, and nothing would throw. Same fallback ProjectFindService performs.
    /// </summary>
    private static ConversationEditSnapshot WithTextRestored(
        ConversationEditSnapshot snap, ConversationPatch patch, string primaryLanguage)
    {
        var byId = (patch.Translations.GetValueOrDefault(primaryLanguage) ?? [])
            .ToDictionary(t => t.NodeId);
        if (byId.Count == 0) return snap;

        return new ConversationEditSnapshot(snap.Nodes.Select(n =>
        {
            if (!string.IsNullOrEmpty(n.DefaultText) && !string.IsNullOrEmpty(n.FemaleText))
                return n;
            if (!byId.TryGetValue(n.NodeId, out var t)) return n;
            return n with
            {
                DefaultText = string.IsNullOrEmpty(n.DefaultText) ? t.DefaultText ?? "" : n.DefaultText,
                FemaleText  = string.IsNullOrEmpty(n.FemaleText)  ? t.FemaleText  ?? "" : n.FemaleText,
            };
        }).ToList());
    }
}
