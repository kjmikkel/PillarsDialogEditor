using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class DiffEngine
{
    public static ConversationPatch Diff(
        string conversationName,
        ConversationEditSnapshot baseSnap,
        ConversationEditSnapshot currentSnap)
    {
        var baseById    = baseSnap.Nodes.ToDictionary(n => n.NodeId);
        var currentById = currentSnap.Nodes.ToDictionary(n => n.NodeId);

        var added   = currentSnap.Nodes.Where(n => !baseById.ContainsKey(n.NodeId)).ToList();
        var deleted = baseSnap.Nodes.Select(n => n.NodeId)
                              .Where(id => !currentById.ContainsKey(id)).ToList();
        var modified = new List<NodeModification>();

        foreach (var current in currentSnap.Nodes)
        {
            if (!baseById.TryGetValue(current.NodeId, out var @base)) continue;
            var mod = DiffNode(@base, current);
            if (mod is not null) modified.Add(mod);
        }

        return new ConversationPatch(
            conversationName,
            ConversationPatch.CurrentSchemaVersion,
            added,
            deleted,
            modified);
    }

    private static NodeModification? DiffNode(NodeEditSnapshot @base, NodeEditSnapshot current)
    {
        var changes = new Dictionary<string, FieldChange>();

        TryAddChange(changes, "IsPlayerChoice", @base.IsPlayerChoice,    current.IsPlayerChoice);
        TryAddChange(changes, "SpeakerGuid",    @base.SpeakerGuid,       current.SpeakerGuid);
        TryAddChange(changes, "ListenerGuid",   @base.ListenerGuid,      current.ListenerGuid);
        TryAddChange(changes, "DefaultText",    @base.DefaultText,       current.DefaultText);
        TryAddChange(changes, "FemaleText",     @base.FemaleText,        current.FemaleText);
        TryAddChange(changes, "DisplayType",    @base.DisplayType,       current.DisplayType);
        TryAddChange(changes, "Persistence",    @base.Persistence,       current.Persistence);
        TryAddChange(changes, "ActorDirection", @base.ActorDirection,    current.ActorDirection);
        TryAddChange(changes, "Comments",       @base.Comments,          current.Comments);
        TryAddChange(changes, "ExternalVO",     @base.ExternalVO,        current.ExternalVO);
        TryAddChange(changes, "HasVO",          @base.HasVO,             current.HasVO);
        TryAddChange(changes, "HideSpeaker",    @base.HideSpeaker,       current.HideSpeaker);

        var baseLinks    = @base.Links.ToDictionary(l => l.ToNodeId);
        var currentLinks = current.Links.ToDictionary(l => l.ToNodeId);

        var addedLinks   = current.Links.Where(l => !baseLinks.ContainsKey(l.ToNodeId)).ToList();
        var deletedLinks = @base.Links
            .Where(l => !currentLinks.ContainsKey(l.ToNodeId))
            .Select(l => new DeletedLink(l.ToNodeId, l.HasConditions))
            .ToList();
        var modifiedLinks = current.Links
            .Where(l => baseLinks.TryGetValue(l.ToNodeId, out var b) &&
                        (b.RandomWeight != l.RandomWeight || b.QuestionNodeTextDisplay != l.QuestionNodeTextDisplay))
            .Select(l => new ModifiedLink(l.ToNodeId, l.RandomWeight, l.QuestionNodeTextDisplay))
            .ToList();

        if (changes.Count == 0 && addedLinks.Count == 0 && deletedLinks.Count == 0 && modifiedLinks.Count == 0)
            return null;

        return new NodeModification(current.NodeId, changes, addedLinks, deletedLinks, modifiedLinks);
    }

    private static void TryAddChange<T>(
        Dictionary<string, FieldChange> changes,
        string fieldName,
        T baseValue,
        T currentValue)
    {
        var fromJson = JsonSerializer.Serialize(baseValue);
        var toJson   = JsonSerializer.Serialize(currentValue);
        if (fromJson != toJson)
            changes[fieldName] = new FieldChange(fromJson, toJson);
    }
}
