namespace DialogEditor.Core.Models;

public record ConversationNode(
    int NodeId,
    bool IsPlayerChoice,
    SpeakerCategory SpeakerCategory,
    string SpeakerGuid,
    string ListenerGuid,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<ConditionNode> Conditions,
    IReadOnlyList<string> Scripts,
    string DisplayType,
    string Persistence,
    string ActorDirection = "",
    string Comments = "",
    string ExternalVO = "",
    bool HasVO = false,
    bool HideSpeaker = false
)
{
    public bool HasConditions     => Conditions.Count > 0;
    public bool HasScripts        => Scripts.Count > 0;

    // Backward-compat display properties — derived from structured tree
    public IReadOnlyList<string> ConditionStrings
        => Conditions.SelectMany(c => c.Leaves()).Select(c => c.Format()).ToList();

    public string ConditionExpression => Conditions.FormatTree();
}
