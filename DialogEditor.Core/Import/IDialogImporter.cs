using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

public record ImportedConversation(
    string SuggestedName,
    IReadOnlyList<NodeEditSnapshot> Nodes,
    IReadOnlyList<NodeTranslation>  Texts
);

public interface IDialogImporter
{
    /// File extensions this importer handles, e.g. [".csv"]
    string[] FileExtensions { get; }

    /// Parse the file at <paramref name="path"/> into an ImportedConversation.
    ImportedConversation Import(string path);
}
