using System.Text.Json;
using System.Text.Json.Nodes;

namespace DialogEditor.Core.Export;

public class JsonDialogExporter : IDialogExporter
{
    public string FileExtension => ".json";

    public void Export(ConversationExport conversation, string path)
    {
        var nodesArray = new JsonArray(conversation.Nodes.Select(n => (JsonNode)new JsonObject
        {
            ["id"]              = n.NodeId,
            ["speakerCategory"] = n.SpeakerCategory.ToString(),
            ["defaultText"]     = n.DefaultText,
            ["femaleText"]      = n.FemaleText,
            ["links"]           = new JsonArray(
                n.Links.Select(l => (JsonNode)JsonValue.Create(l.ToNodeId)).ToArray()),
            ["displayType"]     = n.DisplayType,
            ["persistence"]     = n.Persistence,
        }).ToArray());

        var doc = new JsonObject
        {
            ["name"]  = conversation.Name,
            ["nodes"] = nodesArray,
        };

        File.WriteAllText(path,
            doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
