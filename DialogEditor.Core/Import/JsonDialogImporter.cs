using System.Text.Json;
using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

public class JsonDialogImporter : IDialogImporter
{
    public string[] FileExtensions => [".json"];

    public ImportedConversation Import(string path)
    {
        var json = File.ReadAllText(path);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new FormatException("JSON file does not contain a top-level object.");

        if (!root.ContainsKey("nodes"))
            throw new FormatException("JSON file is missing the required 'nodes' array.");

        var nodesArray = root["nodes"]?.AsArray()
            ?? throw new FormatException("JSON file is missing the required 'nodes' array.");

        var suggestedName = root["name"]?.GetValue<string>()
            ?? Path.GetFileNameWithoutExtension(path);

        var nodes = new List<NodeEditSnapshot>();
        var texts = new List<NodeTranslation>();

        foreach (var element in nodesArray)
        {
            var nodeObj = element?.AsObject() ?? new JsonObject();

            var nodeId        = nodeObj["id"]?.GetValue<int>() ?? 0;
            var defaultText   = nodeObj["defaultText"]?.GetValue<string>() ?? "";
            var femaleText    = nodeObj["femaleText"]?.GetValue<string>() ?? "";
            var displayType   = nodeObj["displayType"]?.GetValue<string>() ?? "Conversation";
            var persistence   = nodeObj["persistence"]?.GetValue<string>() ?? "None";
            var categoryStr   = nodeObj["speakerCategory"]?.GetValue<string>() ?? "";

            var speakerCategory = ParseSpeakerCategory(categoryStr);
            var isPlayerChoice  = speakerCategory == SpeakerCategory.Player;
            var links           = ParseLinks(nodeId, nodeObj["links"]?.AsArray());

            nodes.Add(new NodeEditSnapshot(
                NodeId: nodeId,
                IsPlayerChoice: isPlayerChoice,
                SpeakerCategory: speakerCategory,
                SpeakerGuid: "",
                ListenerGuid: "",
                DefaultText: defaultText,
                FemaleText: femaleText,
                DisplayType: displayType,
                Persistence: persistence,
                ActorDirection: "",
                Comments: "",
                ExternalVO: "",
                HasVO: false,
                HideSpeaker: false,
                Links: links,
                Conditions: [],
                Scripts: []));

            texts.Add(new NodeTranslation(nodeId, defaultText, femaleText));
        }

        return new ImportedConversation(suggestedName, nodes, texts, []);
    }

    private static SpeakerCategory ParseSpeakerCategory(string value) =>
        Enum.TryParse<SpeakerCategory>(value, ignoreCase: true, out var result)
            ? result
            : SpeakerCategory.Npc;

    private static List<LinkEditSnapshot> ParseLinks(int fromNodeId, JsonArray? linksArray)
    {
        var links = new List<LinkEditSnapshot>();
        if (linksArray is null)
            return links;

        foreach (var item in linksArray)
        {
            if (item is not null && item.GetValueKind() == JsonValueKind.Number)
            {
                links.Add(new LinkEditSnapshot(
                    FromNodeId: fromNodeId,
                    ToNodeId: item.GetValue<int>(),
                    RandomWeight: 1f,
                    QuestionNodeTextDisplay: "",
                    HasConditions: false)
                {
                    Conditions = null
                });
            }
        }

        return links;
    }
}
