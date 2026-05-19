namespace DialogEditor.Core.Models;

public record NodeLink(
    int FromNodeId,
    int ToNodeId,
    IReadOnlyList<ConditionNode> Conditions,
    float RandomWeight = 1f,
    string QuestionNodeTextDisplay = ""
)
{
    public bool HasConditions => Conditions.Count > 0;
}
