using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Export;

public record ConversationExport(
    string Name,
    IReadOnlyList<NodeEditSnapshot> Nodes
);

public interface IDialogExporter
{
    /// File extension including the leading dot, e.g. ".csv".
    string FileExtension { get; }

    void Export(ConversationExport conversation, string path);
}
