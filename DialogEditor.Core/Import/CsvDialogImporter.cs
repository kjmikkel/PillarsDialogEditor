using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

public class CsvDialogImporter : IDialogImporter
{
    public string[] FileExtensions => [".csv"];

    public ImportedConversation Import(string path)
    {
        var lines = File.ReadAllLines(path);

        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            throw new FormatException("CSV file is missing a header row.");

        var columns = ParseCsvRow(lines[0]);
        var idx = BuildColumnIndex(columns);

        if (!idx.TryGetValue("nodeid", out int nodeIdCol))
            throw new FormatException("CSV header is missing the required 'NodeId' column.");

        var speakerCategoryCol = idx.GetValueOrDefault("speakercategory", -1);
        var defaultTextCol     = idx.GetValueOrDefault("defaulttext", -1);
        var femaleTextCol      = idx.GetValueOrDefault("femaletext", -1);
        var linksToCol         = idx.GetValueOrDefault("linksto", -1);
        var displayTypeCol     = idx.GetValueOrDefault("displaytype", -1);
        var persistenceCol     = idx.GetValueOrDefault("persistence", -1);

        var nodes = new List<NodeEditSnapshot>();
        var texts = new List<NodeTranslation>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvRow(line);

            var nodeId = int.Parse(GetField(fields, nodeIdCol));
            var speakerCategoryStr = GetField(fields, speakerCategoryCol);
            var defaultText = GetField(fields, defaultTextCol);
            var femaleText = GetField(fields, femaleTextCol);
            var linksToStr = GetField(fields, linksToCol);
            var displayType = GetField(fields, displayTypeCol, "Conversation");
            var persistence = GetField(fields, persistenceCol, "None");

            var speakerCategory = ParseSpeakerCategory(speakerCategoryStr);
            var isPlayerChoice = speakerCategory == SpeakerCategory.Player;

            var links = ParseLinks(nodeId, linksToStr);

            var node = new NodeEditSnapshot(
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
                Scripts: []);

            nodes.Add(node);
            texts.Add(new NodeTranslation(nodeId, defaultText, femaleText));
        }

        var name = Path.GetFileNameWithoutExtension(path);
        return new ImportedConversation(name, nodes, texts);
    }

    private static Dictionary<string, int> BuildColumnIndex(List<string> headers)
    {
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            var key = headers[i].Trim().ToLowerInvariant().Replace(" ", "");
            if (!idx.ContainsKey(key))
                idx[key] = i;
        }
        return idx;
    }

    private static string GetField(List<string> fields, int col, string defaultValue = "")
    {
        if (col < 0 || col >= fields.Count)
            return defaultValue;
        var value = fields[col].Trim();
        return value.Length == 0 ? defaultValue : value;
    }

    // Tolerate unknown values — reject the whole file for one bad field would be too strict.
    private static SpeakerCategory ParseSpeakerCategory(string value) =>
        Enum.TryParse<SpeakerCategory>(value, ignoreCase: true, out var result)
            ? result
            : SpeakerCategory.Npc;

    private static List<LinkEditSnapshot> ParseLinks(int fromNodeId, string linksToStr)
    {
        var links = new List<LinkEditSnapshot>();
        if (string.IsNullOrWhiteSpace(linksToStr))
            return links;

        foreach (var part in linksToStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int toNodeId))
            {
                links.Add(new LinkEditSnapshot(
                    FromNodeId: fromNodeId,
                    ToNodeId: toNodeId,
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

    private static List<string> ParseCsvRow(string line)
    {
        var fields = new List<string>();
        int pos = 0;

        while (pos <= line.Length)
        {
            if (pos == line.Length)
            {
                fields.Add("");
                break;
            }

            if (line[pos] == '"')
            {
                pos++;
                var sb = new System.Text.StringBuilder();
                while (pos < line.Length)
                {
                    if (line[pos] == '"')
                    {
                        pos++;
                        if (pos < line.Length && line[pos] == '"')
                        {
                            sb.Append('"');
                            pos++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(line[pos++]);
                    }
                }
                fields.Add(sb.ToString());
                if (pos < line.Length && line[pos] == ',')
                    pos++;
            }
            else
            {
                int start = pos;
                while (pos < line.Length && line[pos] != ',')
                    pos++;
                fields.Add(line[start..pos]);
                if (pos < line.Length)
                    pos++;
                else
                    break;
            }
        }

        return fields;
    }
}
