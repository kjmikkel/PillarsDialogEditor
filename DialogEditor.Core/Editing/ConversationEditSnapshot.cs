using System.Text.Json.Serialization;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Editing;

public record LinkEditSnapshot(
    int FromNodeId,
    int ToNodeId,
    int RandomWeight,
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
    [property: JsonIgnore] string DefaultText,
    [property: JsonIgnore] string FemaleText,
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
)
{
    /// Which script-family node type this is (issue 167). Non-positional so existing
    /// callers compile; importers and new nodes get the plain ScriptNode default.
    /// Not saved: a project is reloaded by re-parsing the patched game file, whose node
    /// type NodeTypeFamily preserves, so persisting it would only change the project format.
    [JsonIgnore]
    public ScriptNodeKind ScriptKind { get; init; } = ScriptNodeKind.Script;
}

public record ConversationEditSnapshot(IReadOnlyList<NodeEditSnapshot> Nodes);
