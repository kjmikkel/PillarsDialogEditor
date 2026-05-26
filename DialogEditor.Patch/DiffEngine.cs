using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class DiffEngine
{
    public static ConversationPatch Diff(
        string conversationName,
        ConversationEditSnapshot baseSnap,
        ConversationEditSnapshot currentSnap,
        string language)
    {
        var baseById    = baseSnap.Nodes.ToDictionary(n => n.NodeId);
        var currentById = currentSnap.Nodes.ToDictionary(n => n.NodeId);

        // Added nodes: strip text (goes to Translations instead)
        var added   = currentSnap.Nodes
            .Where(n => !baseById.ContainsKey(n.NodeId))
            .Select(n => n with { DefaultText = "", FemaleText = "" })
            .ToList();
        var deleted = baseSnap.Nodes.Select(n => n.NodeId)
                              .Where(id => !currentById.ContainsKey(id)).ToList();
        var modified = new List<NodeModification>();

        foreach (var current in currentSnap.Nodes)
        {
            if (!baseById.TryGetValue(current.NodeId, out var @base)) continue;
            var mod = DiffNode(@base, current);
            if (mod is not null) modified.Add(mod);
        }

        // Build Translations[language]: added nodes + nodes with text changes
        var translationList = new List<NodeTranslation>();

        foreach (var node in currentSnap.Nodes.Where(n => !baseById.ContainsKey(n.NodeId)))
            translationList.Add(new NodeTranslation(node.NodeId, node.DefaultText, node.FemaleText));

        foreach (var current in currentSnap.Nodes)
        {
            if (!baseById.TryGetValue(current.NodeId, out var @base)) continue;
            if (@base.DefaultText != current.DefaultText || @base.FemaleText != current.FemaleText)
                translationList.Add(new NodeTranslation(current.NodeId, current.DefaultText, current.FemaleText));
        }

        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations =
            translationList.Count > 0
                ? new Dictionary<string, IReadOnlyList<NodeTranslation>> { [language] = translationList }
                : new Dictionary<string, IReadOnlyList<NodeTranslation>>();

        return new ConversationPatch(
            conversationName,
            ConversationPatch.CurrentSchemaVersion,
            added,
            deleted,
            modified)
            { Translations = translations };
    }

    private static NodeModification? DiffNode(NodeEditSnapshot @base, NodeEditSnapshot current)
    {
        var changes = new Dictionary<string, FieldChange>();

        TryAddChange(changes, "IsPlayerChoice", @base.IsPlayerChoice,    current.IsPlayerChoice);
        TryAddChange(changes, "SpeakerGuid",    @base.SpeakerGuid,       current.SpeakerGuid);
        TryAddChange(changes, "ListenerGuid",   @base.ListenerGuid,      current.ListenerGuid);
        // DefaultText and FemaleText are now in Translations, not FieldChanges
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
                        (b.RandomWeight != l.RandomWeight ||
                         b.QuestionNodeTextDisplay != l.QuestionNodeTextDisplay ||
                         JsonSerializer.Serialize(b.Conditions) != JsonSerializer.Serialize(l.Conditions)))
            .Select(l =>
            {
                baseLinks.TryGetValue(l.ToNodeId, out var b);
                var condJson = JsonSerializer.Serialize(l.Conditions);
                IReadOnlyList<ConditionNode>? conds =
                    JsonSerializer.Serialize(b?.Conditions) != condJson ? l.Conditions : null;
                return new ModifiedLink(l.ToNodeId, l.RandomWeight, l.QuestionNodeTextDisplay, conds);
            })
            .ToList();

        var baseCondJson    = JsonSerializer.Serialize(@base.Conditions);
        var currentCondJson = JsonSerializer.Serialize(current.Conditions);
        IReadOnlyList<ConditionNode>? updatedConditions =
            baseCondJson != currentCondJson ? current.Conditions : null;

        var baseScriptJson    = JsonSerializer.Serialize(@base.Scripts);
        var currentScriptJson = JsonSerializer.Serialize(current.Scripts);
        IReadOnlyList<ScriptCall>? updatedScripts =
            baseScriptJson != currentScriptJson ? current.Scripts : null;

        if (changes.Count == 0 && addedLinks.Count == 0 && deletedLinks.Count == 0
            && modifiedLinks.Count == 0 && updatedConditions is null && updatedScripts is null)
            return null;

        return new NodeModification(current.NodeId, changes, addedLinks, deletedLinks, modifiedLinks)
            { UpdatedConditions = updatedConditions, UpdatedScripts = updatedScripts };
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
