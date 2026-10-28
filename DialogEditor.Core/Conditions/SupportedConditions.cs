using DialogEditor.Core.Models;

namespace DialogEditor.Core.Conditions;

/// <summary>
/// The condition functions the evaluator understands, keyed by method name. Adding a function
/// is one entry here (evaluation plus the facts it reads) and its tests. Anything absent, with
/// the wrong parameter count, or with unparseable parameters evaluates to Unknown.
/// </summary>
public static class SupportedConditions
{
    private sealed record Entry(
        Func<IReadOnlyList<string>, GameState, Truth> Eval,
        Func<IReadOnlyList<string>, IEnumerable<ConditionFact>> Facts);

    private static readonly IEnumerable<ConditionFact> NoFacts = [];

    private static readonly Dictionary<string, Entry> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Game: Conditionals.IsGlobalValue -> GlobalVariables.GetVariable + CompareInt.
        ["IsGlobalValue"] = new(
            (p, s) => p.Count == 3 && s.Globals.TryGetValue(p[0], out var v)
                ? Compare(v, p[1], p[2]) : Truth.Unknown,
            p => p.Count == 3 ? [new ConditionFact(FactKind.Global, p[0])] : NoFacts),

        ["CompareGlobals"] = new(
            (p, s) => p.Count == 3 && s.Globals.TryGetValue(p[0], out var a) && s.Globals.TryGetValue(p[2], out var b)
                ? Compare(a, p[1], b) : Truth.Unknown,
            p => p.Count == 3
                ? [new ConditionFact(FactKind.Global, p[0]), new ConditionFact(FactKind.Global, p[2])]
                : NoFacts),

        ["HasConversationNodeBeenPlayed"] = new(
            (p, s) => p.Count == 2 && int.TryParse(p[1], out var id) && s.PlayedNodes.TryGetValue((p[0], id), out var played)
                ? FromBool(played) : Truth.Unknown,
            p => p.Count == 2 && int.TryParse(p[1], out var id)
                ? [new ConditionFact(FactKind.PlayedNode, p[0], id)] : NoFacts),

        ["IsCompanionActiveInParty"] = new(
            (p, s) => p.Count == 1 && s.CompanionsInParty.TryGetValue(p[0], out var inParty)
                ? FromBool(inParty) : Truth.Unknown,
            CompanionFacts),

        // "Present" (e.g. in a companion-only scene) is not modelled, so not-in-party can't be decided.
        ["IsCompanionActiveInPartyOrPresent"] = new(
            (p, s) => p.Count == 1 && s.CompanionsInParty.TryGetValue(p[0], out var inParty) && inParty
                ? Truth.True : Truth.Unknown,
            CompanionFacts),

        ["IsAnyCompanionActiveInParty"] = new(
            (p, s) => s.CompanionsInParty.Values.Any(v => v) ? Truth.True
                : s.NoCompanionsInParty ? Truth.False : Truth.Unknown,
            _ => NoFacts),

        // Game: QuestManager.HasQuestStarted -> Quest.IsStarted (state 0 visited).
        ["HasQuestStarted"] = Quest(st => FromBool(st != QuestStatus.NotStarted)),

        // Game: QuestManager.IsQuestActive -> ActiveQuests.ContainsKey. A finished quest is removed
        // (Quest.DetermineIfQuestHasEnded -> EndQuest); whether a failed one is too is unverified.
        ["IsQuestActive"] = Quest(st => st switch
        {
            QuestStatus.Active => Truth.True,
            QuestStatus.Failed => Truth.Unknown,
            _ => Truth.False,
        }),

        // Game: QuestManager.IsQuestCompleted -> IsComplete() && !IsQuestFailed().
        ["IsQuestCompleted"] = Quest(st => FromBool(st == QuestStatus.Completed)),

        // Game: QuestManager.IsQuestFailed -> QuestTrackers[id].Failed.
        ["IsQuestFailed"] = Quest(st => FromBool(st == QuestStatus.Failed)),

        ["IsInCombat"] = new(
            (_, s) => s.InCombat is { } c ? FromBool(c) : Truth.Unknown,
            _ => [new ConditionFact(FactKind.InCombat, string.Empty)]),

        ["AlwaysTrue"] = new((_, _) => Truth.True, _ => NoFacts),
        ["AlwaysFalse"] = new((_, _) => Truth.False, _ => NoFacts),
        ["Always"] = new(
            (p, _) => p.Count == 1 && bool.TryParse(p[0], out var b) ? FromBool(b) : Truth.Unknown,
            _ => NoFacts),
    };

    /// The method name of a call: the text between the first space and '(' in its FullName.
    public static string MethodName(ConditionLeaf leaf) => leaf.GetMethodName();

    public static bool IsSupported(string methodName) => Table.ContainsKey(methodName);

    /// Evaluates one call against the state. Ignores <c>leaf.Not</c>; the evaluator applies it.
    public static Truth Evaluate(ConditionLeaf leaf, GameState state)
        => Table.TryGetValue(MethodName(leaf), out var entry) ? entry.Eval(leaf.Parameters, state) : Truth.Unknown;

    /// The facts a call reads (empty for unsupported functions or malformed parameters).
    internal static IEnumerable<ConditionFact> FactsOf(ConditionLeaf leaf)
        => Table.TryGetValue(MethodName(leaf), out var entry) ? entry.Facts(leaf.Parameters) : NoFacts;

    private static IEnumerable<ConditionFact> CompanionFacts(IReadOnlyList<string> p)
        => p.Count == 1 ? [new ConditionFact(FactKind.Companion, p[0])] : NoFacts;

    private static Entry Quest(Func<QuestStatus, Truth> byStatus) => new(
        (p, s) => p.Count == 1 && s.Quests.TryGetValue(p[0], out var st) ? byStatus(st) : Truth.Unknown,
        p => p.Count == 1 ? [new ConditionFact(FactKind.Quest, p[0])] : NoFacts);

    private static Truth FromBool(bool b) => b ? Truth.True : Truth.False;

    // The game's Operator enum; "Equals" is catalogue drift for EqualTo.
    private static Truth Compare(int left, string op, string rightText)
        => int.TryParse(rightText, out var right) ? Compare(left, op, right) : Truth.Unknown;

    private static Truth Compare(int left, string op, int right)
    {
        if (op.Equals("EqualTo", StringComparison.OrdinalIgnoreCase)
            || op.Equals("Equals", StringComparison.OrdinalIgnoreCase)) return FromBool(left == right);
        if (op.Equals("NotEqualTo", StringComparison.OrdinalIgnoreCase)) return FromBool(left != right);
        if (op.Equals("GreaterThan", StringComparison.OrdinalIgnoreCase)) return FromBool(left > right);
        if (op.Equals("LessThan", StringComparison.OrdinalIgnoreCase)) return FromBool(left < right);
        if (op.Equals("GreaterThanOrEqualTo", StringComparison.OrdinalIgnoreCase)) return FromBool(left >= right);
        if (op.Equals("LessThanOrEqualTo", StringComparison.OrdinalIgnoreCase)) return FromBool(left <= right);
        return Truth.Unknown;
    }
}
