using System.Text;

namespace DialogEditor.Core.Export;

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
