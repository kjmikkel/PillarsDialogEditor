namespace DialogEditor.Core.Conditions;

/// <summary>
/// The writer's assumed game state. Every fact is optional: absent means "not set", which
/// makes any condition reading it evaluate to <see cref="Truth.Unknown"/>.
/// </summary>
public sealed class GameState
{
    private sealed class PlayedKeyComparer : IEqualityComparer<(string Conversation, int NodeId)>
    {
        public bool Equals((string Conversation, int NodeId) a, (string Conversation, int NodeId) b)
            => a.NodeId == b.NodeId
               && string.Equals(a.Conversation, b.Conversation, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Conversation, int NodeId) k)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Conversation ?? string.Empty), k.NodeId);
    }

    public IDictionary<string, int> Globals { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, bool> CompanionsInParty { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public bool NoCompanionsInParty { get; set; }
    public IDictionary<(string Conversation, int NodeId), bool> PlayedNodes { get; }
        = new Dictionary<(string Conversation, int NodeId), bool>(new PlayedKeyComparer());
    public IDictionary<string, QuestStatus> Quests { get; } = new Dictionary<string, QuestStatus>(StringComparer.OrdinalIgnoreCase);
    public bool? InCombat { get; set; }

    public bool HasAnyFact =>
        Globals.Count > 0 || CompanionsInParty.Count > 0 || NoCompanionsInParty
        || PlayedNodes.Count > 0 || Quests.Count > 0 || InCombat.HasValue;

    public event EventHandler? Changed;

    /// Call after mutating any collection or property so listeners refresh.
    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Clear()
    {
        Globals.Clear();
        CompanionsInParty.Clear();
        NoCompanionsInParty = false;
        PlayedNodes.Clear();
        Quests.Clear();
        InCombat = null;
        NotifyChanged();
    }
}
