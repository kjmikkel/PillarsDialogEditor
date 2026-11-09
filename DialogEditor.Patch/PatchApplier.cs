using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class PatchApplier
{
    public static ConversationEditSnapshot Apply(
        ConversationEditSnapshot baseSnap,
        ConversationPatch patch,
        bool ignoreConflicts = false)
    {
        var nodeMap = baseSnap.Nodes.ToDictionary(n => n.NodeId);

        // Deleted nodes
        foreach (var id in patch.DeletedNodeIds)
            nodeMap.Remove(id);

        // Added nodes
        foreach (var node in patch.AddedNodes)
            nodeMap[node.NodeId] = node;

        // Modified nodes
        foreach (var mod in patch.ModifiedNodes)
        {
            if (!nodeMap.TryGetValue(mod.NodeId, out var node))
                continue;
            nodeMap[mod.NodeId] = ApplyModification(node, mod, ignoreConflicts);
        }

        return new ConversationEditSnapshot(nodeMap.Values.ToList());
    }

    public static ConversationEditSnapshot ApplyAll(
        ConversationEditSnapshot baseSnap,
        IEnumerable<ConversationPatch> patches,
        bool ignoreConflicts = false)
    {
        var current = baseSnap;
        foreach (var patch in patches)
            current = Apply(current, patch, ignoreConflicts);
        return current;
    }

    private static NodeEditSnapshot ApplyModification(
        NodeEditSnapshot node,
        NodeModification mod,
        bool ignoreConflicts = false)
    {
        var isPlayerChoice = node.IsPlayerChoice;
        var category       = node.SpeakerCategory;
        var speakerGuid    = node.SpeakerGuid;
        var listenerGuid   = node.ListenerGuid;
        var displayType    = node.DisplayType;
        var persistence    = node.Persistence;
        var actorDirection = node.ActorDirection;
        var comments       = node.Comments;
        var externalVO     = node.ExternalVO;
        var hasVO          = node.HasVO;
        var hideSpeaker    = node.HideSpeaker;

        foreach (var (field, change) in mod.FieldChanges)
        {
            var actualJson = field switch
            {
                "IsPlayerChoice" => JsonSerializer.Serialize(isPlayerChoice),
                "SpeakerCategory" => JsonSerializer.Serialize(category.ToString()),
                "SpeakerGuid"    => JsonSerializer.Serialize(speakerGuid),
                "ListenerGuid"   => JsonSerializer.Serialize(listenerGuid),
                "DisplayType"    => JsonSerializer.Serialize(displayType),
                "Persistence"    => JsonSerializer.Serialize(persistence),
                "ActorDirection" => JsonSerializer.Serialize(actorDirection),
                "Comments"       => JsonSerializer.Serialize(comments),
                "ExternalVO"     => JsonSerializer.Serialize(externalVO),
                "HasVO"          => JsonSerializer.Serialize(hasVO),
                "HideSpeaker"    => JsonSerializer.Serialize(hideSpeaker),
                _ => throw new InvalidOperationException($"Unknown field: {field}")
            };

            if (actualJson != change.From && !ignoreConflicts)
                throw new PatchConflictException(node.NodeId, field, change.From, actualJson);

            switch (field)
            {
                case "IsPlayerChoice": isPlayerChoice = JsonSerializer.Deserialize<bool>(change.To); break;
                case "SpeakerCategory": category      = Enum.Parse<SpeakerCategory>(JsonSerializer.Deserialize<string>(change.To)!); break;
                case "SpeakerGuid":    speakerGuid    = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "ListenerGuid":   listenerGuid   = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "DisplayType":    displayType    = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "Persistence":    persistence    = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "ActorDirection": actorDirection = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "Comments":       comments       = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "ExternalVO":     externalVO     = JsonSerializer.Deserialize<string>(change.To)!; break;
                case "HasVO":          hasVO          = JsonSerializer.Deserialize<bool>(change.To); break;
                case "HideSpeaker":    hideSpeaker    = JsonSerializer.Deserialize<bool>(change.To); break;
            }
        }

        // Patches saved before issue 160 record a Node Type change as IsPlayerChoice alone. The
        // serializers write the type from the category, so derive it, as the editor now does.
        if (mod.FieldChanges.ContainsKey("IsPlayerChoice") && !mod.FieldChanges.ContainsKey("SpeakerCategory"))
            category = isPlayerChoice ? SpeakerCategory.Player
                     : category == SpeakerCategory.Player ? SpeakerCategory.Npc
                     : category;

        var deletedToIds = mod.DeletedLinks.Select(d => d.ToNodeId).ToHashSet();
        var modifiedById = mod.ModifiedLinks.ToDictionary(m => m.ToNodeId);
        var links = node.Links
            .Where(l => !deletedToIds.Contains(l.ToNodeId))
            .Select(l => modifiedById.TryGetValue(l.ToNodeId, out var m)
                ? l with
                {
                    RandomWeight            = m.RandomWeight,
                    QuestionNodeTextDisplay = m.QuestionNodeTextDisplay,
                    Conditions              = m.Conditions ?? l.Conditions,
                }
                : l)
            .Concat(mod.AddedLinks)
            .ToList();

        // Stable sort by position in the record. A link the record doesn't name (say, one a game update
        // or another patch added) goes after the listed ones in its existing order: never drop a link.
        // Ids with no link are ignored (issue 359).
        if (mod.UpdatedLinkOrder is { } order)
        {
            var rank = new Dictionary<int, int>();
            foreach (var id in order) rank.TryAdd(id, rank.Count);
            links = links.OrderBy(l => rank.TryGetValue(l.ToNodeId, out var r) ? r : int.MaxValue).ToList();
        }

        var conditions = mod.UpdatedConditions ?? node.Conditions;
        var scripts    = mod.UpdatedScripts    ?? node.Scripts;

        return node with
        {
            IsPlayerChoice = isPlayerChoice,
            SpeakerCategory = category,
            SpeakerGuid    = speakerGuid,
            ListenerGuid   = listenerGuid,
            DisplayType    = displayType,
            Persistence    = persistence,
            ActorDirection = actorDirection,
            Comments       = comments,
            ExternalVO     = externalVO,
            HasVO          = hasVO,
            HideSpeaker    = hideSpeaker,
            Links          = links,
            Conditions     = conditions,
            Scripts        = scripts,
        };
    }
}
