namespace DialogEditor.Core.Models;

public record ConversationNode(
    int NodeId,
    bool IsPlayerChoice,
    SpeakerCategory SpeakerCategory,
    string SpeakerGuid,
    string ListenerGuid,
    IReadOnlyList<NodeLink> Links,
    IReadOnlyList<ConditionNode> Conditions,
    IReadOnlyList<ScriptCall> Scripts,
    string DisplayType,
    string Persistence,
    string ActorDirection = "",
    string Comments = "",
    string ExternalVO = "",
    bool HasVO = false,
    bool HideSpeaker = false
)
{
    public bool HasConditions => Conditions.Count > 0;
    public bool HasScripts    => Scripts.Count > 0;

    // Backward-compat display helpers — derived from the structured list
    public IReadOnlyList<string> ConditionStrings
        => Conditions.SelectMany(c => c.Leaves()).Select(c => c.Format()).ToList();

    public string ConditionExpression => Conditions.FormatTree();

    // Flat display strings with category prefix for the read-only detail panel
    public IReadOnlyList<string> ScriptDisplayStrings
        => Scripts.Select(s =>
        {
            var prefix = s.Category switch
            {
                ScriptCategory.Enter  => "[Enter]",
                ScriptCategory.Exit   => "[Exit]",
                ScriptCategory.Update => "[Update]",
                _                     => "[Script]",
            };
            return $"{prefix} {s.Format()}";
        }).ToList();
}
