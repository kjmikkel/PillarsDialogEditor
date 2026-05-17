using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class ConversationSnapshotBuilder
{
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
                l.QuestionNodeTextDisplay, l.HasConditions)).ToList());
    }
}
