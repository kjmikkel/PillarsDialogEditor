using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class ConversationSnapshotBuilder
{
    /// Reconstructs a Conversation from a snapshot. Used to restore the canvas
    /// state for new (not-yet-on-disk) conversations when reopening a project.
    /// When <paramref name="translations"/> is provided, node text is sourced from
    /// it (since <see cref="NodeEditSnapshot.DefaultText"/> is [JsonIgnore] and
    /// does not survive serialization).
    public static Conversation ToConversation(
        string name,
        ConversationEditSnapshot snap,
        IReadOnlyList<NodeTranslation>? translations = null)
    {
        var textById = translations?.ToDictionary(t => t.NodeId)
                      ?? new Dictionary<int, NodeTranslation>();

        var nodes = snap.Nodes.Select(n => new ConversationNode(
            NodeId:          n.NodeId,
            IsPlayerChoice:  n.IsPlayerChoice,
            SpeakerCategory: n.SpeakerCategory,
            SpeakerGuid:     n.SpeakerGuid,
            ListenerGuid:    n.ListenerGuid,
            Links:           n.Links.Select(l => new NodeLink(
                                 l.FromNodeId, l.ToNodeId,
                                 l.Conditions ?? [], l.RandomWeight,
                                 l.QuestionNodeTextDisplay)).ToList(),
            Conditions:      n.Conditions,
            Scripts:         n.Scripts,
            DisplayType:     n.DisplayType,
            Persistence:     n.Persistence,
            ActorDirection:  n.ActorDirection,
            Comments:        n.Comments,
            ExternalVO:      n.ExternalVO,
            HasVO:           n.HasVO,
            HideSpeaker:     n.HideSpeaker)).ToList();

        var strings = new StringTable(snap.Nodes.Select(n =>
        {
            textById.TryGetValue(n.NodeId, out var t);
            return new StringEntry(n.NodeId,
                t?.DefaultText ?? n.DefaultText,
                t?.FemaleText  ?? n.FemaleText);
        }).ToList());

        return new Conversation(name, nodes, strings);
    }

    public static ConversationEditSnapshot Build(Conversation conversation) =>
        new(conversation.Nodes.Select(n => BuildNode(n, conversation.Strings)).ToList());

    private static NodeEditSnapshot BuildNode(ConversationNode node, StringTable strings)
    {
        var entry = strings.Get(node.NodeId);
        return new NodeEditSnapshot(
            node.NodeId,
            node.IsPlayerChoice,
            node.SpeakerCategory,
            node.SpeakerGuid,
            node.ListenerGuid,
            entry?.DefaultText ?? string.Empty,
            entry?.FemaleText  ?? string.Empty,
            node.DisplayType,
            node.Persistence,
            node.ActorDirection,
            node.Comments,
            node.ExternalVO,
            node.HasVO,
            node.HideSpeaker,
            node.Links.Select(l => new LinkEditSnapshot(
                l.FromNodeId, l.ToNodeId, l.RandomWeight,
                l.QuestionNodeTextDisplay, l.HasConditions)
                { Conditions = l.Conditions }).ToList(),
            node.Conditions,
            node.Scripts);
    }
}
