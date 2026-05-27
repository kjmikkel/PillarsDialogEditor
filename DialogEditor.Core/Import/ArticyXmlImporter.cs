using System.Xml.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

public class ArticyXmlImporter : IDialogImporter
{
    public string[] FileExtensions => [".xml"];

    public ImportedConversation Import(string path)
    {
        var doc = XDocument.Load(path);

        if (doc.Root?.Name.LocalName != "ArticyExport")
            throw new FormatException("File is not an Articy XML export — root element must be 'ArticyExport'.");

        var models = doc.Descendants("Models").FirstOrDefault();

        var suggestedName = models?
            .Elements("Dialogue")
            .FirstOrDefault()
            ?.Attribute("TechnicalName")?.Value;

        if (string.IsNullOrWhiteSpace(suggestedName))
            suggestedName = Path.GetFileNameWithoutExtension(path);

        var entities = BuildEntityMap(models);

        var fragmentElements = models?.Elements("DialogueFragment").ToList()
            ?? [];

        var idMap = BuildIdMap(fragmentElements);

        var nodes = new List<NodeEditSnapshot>();
        var texts = new List<NodeTranslation>();

        foreach (var frag in fragmentElements)
        {
            var articyId = frag.Attribute("Id")?.Value ?? "";
            var intId = idMap[articyId];

            var props = frag.Element("Properties");
            var defaultText = props?.Element("Text")?.Value ?? "";

            var speakerId = props?.Element("Speaker")?.Attribute("Id")?.Value;
            var (speakerCategory, isPlayerChoice) = ResolveSpeaker(speakerId, entities);

            var links = BuildLinks(frag, intId, idMap);

            var node = new NodeEditSnapshot(
                NodeId: intId,
                IsPlayerChoice: isPlayerChoice,
                SpeakerCategory: speakerCategory,
                SpeakerGuid: "",
                ListenerGuid: "",
                DefaultText: defaultText,
                FemaleText: "",
                DisplayType: "Conversation",
                Persistence: "None",
                ActorDirection: "",
                Comments: "",
                ExternalVO: "",
                HasVO: false,
                HideSpeaker: false,
                Links: links,
                Conditions: [],
                Scripts: []);

            nodes.Add(node);
            texts.Add(new NodeTranslation(intId, defaultText, ""));
        }

        return new ImportedConversation(suggestedName, nodes, texts);
    }

    private static Dictionary<string, (string TechnicalName, string DisplayName)> BuildEntityMap(XElement? models)
    {
        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        if (models is null) return map;

        foreach (var entity in models.Elements("Entity"))
        {
            var id = entity.Attribute("Id")?.Value;
            if (id is null) continue;
            var tech = entity.Attribute("TechnicalName")?.Value ?? "";
            var display = entity.Attribute("DisplayName")?.Value ?? "";
            map[id] = (tech, display);
        }

        return map;
    }

    // Sort all fragment IDs lexicographically and assign 1, 2, 3... for a stable mapping.
    private static Dictionary<string, int> BuildIdMap(List<XElement> fragments)
    {
        var ids = fragments
            .Select(f => f.Attribute("Id")?.Value ?? "")
            .Where(id => id.Length > 0)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ids.Count; i++)
            map[ids[i]] = i + 1;

        return map;
    }

    private static (SpeakerCategory Category, bool IsPlayerChoice) ResolveSpeaker(
        string? speakerId,
        Dictionary<string, (string TechnicalName, string DisplayName)> entities)
    {
        if (speakerId is null || !entities.TryGetValue(speakerId, out var entity))
            return (SpeakerCategory.Npc, false);

        if (entity.TechnicalName.Contains("player", StringComparison.OrdinalIgnoreCase))
            return (SpeakerCategory.Player, true);

        if (entity.TechnicalName.Contains("narrator", StringComparison.OrdinalIgnoreCase))
            return (SpeakerCategory.Narrator, false);

        return (SpeakerCategory.Npc, false);
    }

    private static List<LinkEditSnapshot> BuildLinks(
        XElement fragment,
        int fromIntId,
        Dictionary<string, int> idMap)
    {
        var links = new List<LinkEditSnapshot>();

        var outgoing = fragment
            .Element("Connections")
            ?.Element("OutgoingConnections");

        if (outgoing is null) return links;

        foreach (var conn in outgoing.Elements("Connection"))
        {
            var targetArticyId = conn.Attribute("Target")?.Value;
            if (targetArticyId is null) continue;
            if (!idMap.TryGetValue(targetArticyId, out int toIntId)) continue;

            links.Add(new LinkEditSnapshot(
                FromNodeId: fromIntId,
                ToNodeId: toIntId,
                RandomWeight: 1f,
                QuestionNodeTextDisplay: "",
                HasConditions: false)
            {
                Conditions = null
            });
        }

        return links;
    }
}
