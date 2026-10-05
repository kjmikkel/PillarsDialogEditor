namespace DialogEditor.Core.Models;

public record NodeLink(
    int FromNodeId,
    int ToNodeId,
    IReadOnlyList<ConditionNode> Conditions,
    int RandomWeight = 1,
    string QuestionNodeTextDisplay = ""
)
{
    public bool HasConditions => Conditions.Count > 0;
}
