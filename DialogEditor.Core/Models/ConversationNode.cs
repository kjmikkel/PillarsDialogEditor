namespace DialogEditor.Core.Models;

public record ConversationNode(
    int NodeId,
    bool IsPlayerChoice,
    string SpeakerGuid,
    string ListenerGuid,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<string> ConditionStrings,
    int ScriptCount,
    string DisplayType,
    string Persistence
)
{
    public bool HasConditions => ConditionStrings.Count > 0;
    public bool HasScripts => ScriptCount > 0;
}
