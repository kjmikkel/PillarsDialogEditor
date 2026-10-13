namespace DialogEditor.Core.GameData;

public record ConversationFile(
    string Name,
    string FolderPath,
    string ConversationPath,
    string StringTablePath,
    // The data folder the conversation was read from (issue 198): "exported" for the base
    // game, "lax2_exported" etc. for PoE2 DLC. PoE1 has one, so it is always the base.
    string DataRoot = ConversationFile.BaseDataRoot
)
{
    public const string BaseDataRoot = "exported";
}
