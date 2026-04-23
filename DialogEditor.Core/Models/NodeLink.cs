namespace DialogEditor.Core.Models;

public record NodeLink(
    int FromNodeId,
    int ToNodeId,
    bool HasConditions,
    float RandomWeight = 1f,
    string QuestionNodeTextDisplay = ""
);
