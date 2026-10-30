using DialogEditor.Core.Conditions;

namespace DialogEditor.Patch;

/// One played-node fact: whether the writer assumes node <see cref="NodeId"/> of
/// <see cref="Conversation"/> has been played.
public record PlayedNodeFact(string Conversation, int NodeId, bool Played);

/// <summary>
/// The Game State panel's assumed state as saved in a project (GitHub issue 344). Editor
/// metadata only, never written to game files. Every field is optional so a file holding only
/// some facts, or written by an older editor, loads cleanly.
/// </summary>
/// <remarks>
/// A plain DTO rather than <see cref="GameState"/> itself: the live state keys played nodes by
/// a tuple and relies on per-collection comparers, which JSON can't carry; <see cref="ApplyTo"/>
/// rebuilds them. Quest statuses are stored by name, not ordinal, because this serializer
/// writes enums as numbers and a reordered <see cref="QuestStatus"/> would misread old files.
/// </remarks>
public record GameStateSnapshot(
    IReadOnlyDictionary<string, int>? Globals = null,
    IReadOnlyDictionary<string, bool>? CompanionsInParty = null,
    bool NoCompanionsInParty = false,
    IReadOnlyList<PlayedNodeFact>? PlayedNodes = null,
    IReadOnlyDictionary<string, string>? Quests = null,
    bool? InCombat = null)
{
    /// The state as a snapshot; null when it holds no fact, so a clean project saves no state.
    public static GameStateSnapshot? From(GameState state)
    {
        if (!state.HasAnyFact) return null;
        return new GameStateSnapshot(
            state.Globals.Count > 0 ? new Dictionary<string, int>(state.Globals, StringComparer.Ordinal) : null,
            state.CompanionsInParty.Count > 0 ? new Dictionary<string, bool>(state.CompanionsInParty) : null,
            state.NoCompanionsInParty,
            state.PlayedNodes.Count > 0
                ? state.PlayedNodes.Select(kv => new PlayedNodeFact(kv.Key.Conversation, kv.Key.NodeId, kv.Value)).ToList()
                : null,
            state.Quests.Count > 0 ? state.Quests.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) : null,
            state.InCombat);
    }

    /// <summary>
    /// Replaces everything in <paramref name="state"/> with <paramref name="snapshot"/> (null
    /// clears it), then raises <see cref="GameState.Changed"/> once. A quest status this build
    /// doesn't know (written by a newer editor) leaves that quest unset.
    /// </summary>
    public static void ApplyTo(GameStateSnapshot? snapshot, GameState state)
    {
        state.Globals.Clear();
        state.CompanionsInParty.Clear();
        state.PlayedNodes.Clear();
        state.Quests.Clear();
        state.NoCompanionsInParty = snapshot?.NoCompanionsInParty ?? false;
        state.InCombat = snapshot?.InCombat;
        if (snapshot is not null)
        {
            foreach (var (k, v) in snapshot.Globals ?? new Dictionary<string, int>()) state.Globals[k] = v;
            foreach (var (k, v) in snapshot.CompanionsInParty ?? new Dictionary<string, bool>()) state.CompanionsInParty[k] = v;
            foreach (var p in snapshot.PlayedNodes ?? []) state.PlayedNodes[(p.Conversation, p.NodeId)] = p.Played;
            foreach (var (k, v) in snapshot.Quests ?? new Dictionary<string, string>())
                if (Enum.TryParse<QuestStatus>(v, ignoreCase: false, out var status) && Enum.IsDefined(status))
                    state.Quests[k] = status;
        }
        state.NotifyChanged();
    }
}
