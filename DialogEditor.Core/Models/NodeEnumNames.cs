namespace DialogEditor.Core.Models;

/// <summary>
/// The names the editor stores for a node's DisplayType and Persistence (issue 132). They
/// are the game's own enum member names, in OEIFormats' declaration order, and mean the same
/// in both games: PoE1's XML stores the names, PoE2's JSON stores the position in these
/// lists (see Poe2EnumMaps). The detail pane offers exactly these, and never a localised
/// label, so the model holds a value the serializers recognise whatever the UI language.
/// </summary>
public static class NodeEnumNames
{
    // OEIFormats.FlowCharts.Conversations.DisplayType
    public static IReadOnlyList<string> DisplayTypes { get; } = ["Hidden", "Conversation", "Bark", "Overlay"];

    // OEIFormats.FlowCharts.PersistenceType
    public static IReadOnlyList<string> Persistences { get; } = ["None", "OnceEver", "OncePerConversation", "MarkAsRead"];

    // OEIFormats.FlowCharts.Conversations.DialogueLink.QuestionNodeDisplayType — a link's
    // setting, kept here so all three enums share one convention (issue 139). PoE1 writes
    // the name, which XmlSerializer accepts only when it is exactly one of these.
    public static IReadOnlyList<string> QuestionNodeTextDisplays { get; } = ["ShowOnce", "ShowAlways", "ShowNever"];

    /// Before issue 139 the editor stored "Always"/"Never", names neither game has, and a
    /// patch saved then still carries them. Both serializers read them through this, so
    /// such a patch saves as the game's name instead of breaking the conversation.
    public static string QuestionNodeTextDisplayName(string stored) => stored switch
    {
        "Always" => "ShowAlways",
        "Never"  => "ShowNever",
        _        => stored
    };
}
