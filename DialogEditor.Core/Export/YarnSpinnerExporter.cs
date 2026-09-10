using System.Text;

using DialogEditor.Core.Localisation;

namespace DialogEditor.Core.Export;

// Every literal here is Yarn syntax: "title:", "---", "-> ", "===". YarnSpinnerImporter
// reads these files back, and Yarn Spinner itself parses them, so a translated keyword
// would produce a file neither can load.
[NotLocalised("Yarn Spinner file syntax — round-tripped through YarnSpinnerImporter")]
public class YarnSpinnerExporter : IDialogExporter
{
    public string FileExtension => ".yarn";

    public void Export(ConversationExport conversation, string path)
    {
        var sb = new StringBuilder();

        foreach (var node in conversation.Nodes)
        {
            sb.AppendLine($"title: {node.NodeId}");
            sb.AppendLine("---");

            if (node.IsPlayerChoice)
            {
                if (node.Links.Count > 0)
                {
                    foreach (var link in node.Links)
                        sb.AppendLine($"-> {node.DefaultText} [[{link.ToNodeId}]]");
                }
                else
                {
                    sb.AppendLine($"-> {node.DefaultText}");
                }
            }
            else
            {
                sb.AppendLine($"{node.SpeakerCategory}: {node.DefaultText}");
            }

            sb.AppendLine("===");
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
