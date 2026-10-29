using DialogEditor.Core.Models;

namespace DialogEditor.Core.Conditions;

/// <summary>
/// Whether a link can ever be taken under the writer's assumed <see cref="GameState"/> (#32).
///
/// A link is followed only if BOTH its own conditions and its target node's conditions pass:
/// PoE2's FlowChart.GetNextNode and Conversation.GetPlayerResponses check the link's
/// conditionals and then the target node's. PoE1 links carry no conditionals, so the parser
/// hands this an empty link list and only the target node decides.
///
/// Only a definite <see cref="Truth.False"/> blocks. Unknown means the state cannot settle the
/// condition, and dropping a link on a guess would hide real content from the stats.
/// </summary>
public static class LinkFiring
{
    public static bool CannotFire(
        IReadOnlyList<ConditionNode> linkConditions, IReadOnlyList<ConditionNode> targetConditions,
        GameState state, string gameId)
    {
        // No assumed facts: nothing can be proven False, and skipping the evaluation keeps
        // the no-state path free.
        if (!state.HasAnyFact) return false;
        return ConditionEvaluator.Evaluate(linkConditions, state, gameId) == Truth.False
            || ConditionEvaluator.Evaluate(targetConditions, state, gameId) == Truth.False;
    }
}
