using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

public record ImportedConversation(
    // Hint for the UI — may be overridden by the user before the conversation is added.
    string SuggestedName,
    IReadOnlyList<NodeEditSnapshot> Nodes,
    IReadOnlyList<NodeTranslation>  Texts
);

public interface IDialogImporter
{
    /// File extensions this importer handles, e.g. [".csv"]
    // Extensions include the leading dot, e.g. ".csv".
    string[] FileExtensions { get; }

    /// Parse the file at <paramref name="path"/> into an ImportedConversation.
    // Throws FormatException if the file content is not valid for this importer.
    ImportedConversation Import(string path);
}
