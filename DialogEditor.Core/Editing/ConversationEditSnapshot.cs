using DialogEditor.Core.Models;

namespace DialogEditor.Core.Editing;

public record LinkEditSnapshot(
    int FromNodeId,
    int ToNodeId,
    float RandomWeight,
    string QuestionNodeTextDisplay,
    bool HasConditions)
{
    // Non-positional so all existing callers continue to compile.
    public IReadOnlyList<ConditionNode>? Conditions { get; init; }
}

public record NodeEditSnapshot(
    int NodeId,
    bool IsPlayerChoice,
    SpeakerCategory SpeakerCategory,
    string SpeakerGuid,
    string ListenerGuid,
    string DefaultText,
    string FemaleText,
    string DisplayType,
    string Persistence,
    string ActorDirection,
    string Comments,
    string ExternalVO,
    bool HasVO,
    bool HideSpeaker,
    IReadOnlyList<LinkEditSnapshot> Links,
    IReadOnlyList<ConditionNode> Conditions,
    IReadOnlyList<ScriptCall> Scripts
);

public record ConversationEditSnapshot(IReadOnlyList<NodeEditSnapshot> Nodes);
