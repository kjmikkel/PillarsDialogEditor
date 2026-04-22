namespace DialogEditor.Core.Models;

public record ConversationNode(
    int NodeId,
    bool IsPlayerChoice,
    SpeakerCategory SpeakerCategory,
    string SpeakerGuid,
    string ListenerGuid,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<string> ConditionStrings,
    IReadOnlyList<string> Scripts,
    string DisplayType,
    string Persistence,
    string ActorDirection = ""
)
{
    public bool HasConditions => ConditionStrings.Count > 0;
    public bool HasScripts => Scripts.Count > 0;
}
