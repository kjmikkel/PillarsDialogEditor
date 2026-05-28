namespace DialogEditor.Core.Export;

public class CsvDialogExporter : IDialogExporter
{
    public string FileExtension => ".csv";

    public void Export(ConversationExport conversation, string path)
    {
        using var writer = new StreamWriter(path, append: false,
            encoding: System.Text.Encoding.UTF8);
        writer.WriteLine(
            "NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence");

        foreach (var node in conversation.Nodes)
        {
            var linksTo = string.Join(";", node.Links.Select(l => l.ToNodeId));
            writer.WriteLine(string.Join(",",
                node.NodeId.ToString(),
                Escape(node.SpeakerCategory.ToString()),
                Escape(node.DefaultText),
                Escape(node.FemaleText),
                Escape(linksTo),
                Escape(node.DisplayType),
                Escape(node.Persistence)));
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') ||
            value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
