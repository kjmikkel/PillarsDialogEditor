using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.GameData;

public interface IGameDataProvider
{
    string GameName { get; }
    string GameId   { get; }   // "poe1" or "poe2" — used to filter the condition catalogue
    IReadOnlyList<string> AvailableLanguages { get; }
    string Language { get; set; }
    IReadOnlyList<ConversationFile> EnumerateConversations();
    Conversation LoadConversation(ConversationFile file);

    ConversationFile? FindConversation(string name) =>
        EnumerateConversations().FirstOrDefault(f => f.Name == name);
    IReadOnlyDictionary<string, string> LoadSpeakerNames();
    void   SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot);
    string GetStringTablePath(ConversationFile file);
    (string ConversationsRoot, string StringTablesRoot) GetBackupRoots();
}
