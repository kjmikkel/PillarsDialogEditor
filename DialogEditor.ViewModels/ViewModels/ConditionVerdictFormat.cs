using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// Shared by Node Details and the link rows: when the verdict line shows, what it says.
internal static class ConditionVerdictFormat
{
    /// Quiet until the writer has set at least one fact, and for lists with no conditions.
    public static bool Applies(GameState? state, IReadOnlyList<ConditionNode> conditions)
        => state is { HasAnyFact: true } && conditions.Count > 0;

    public static Truth Evaluate(GameState? state, IReadOnlyList<ConditionNode> conditions, string gameId)
        => state is null ? Truth.Unknown : ConditionEvaluator.Evaluate(conditions, state, gameId);

    public static string Text(Truth truth) => Loc.Get(truth switch
    {
        Truth.True => "NodeDetail_Verdict_Passes",
        Truth.False => "NodeDetail_Verdict_Fails",
        _ => "NodeDetail_Verdict_Unknown",
    });
}
