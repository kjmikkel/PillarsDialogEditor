using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.GameData;

public interface IGameDataProvider
{
    string GameName { get; }
    IReadOnlyList<string> AvailableLanguages { get; }
    string Language { get; set; }
    IReadOnlyList<ConversationFile> EnumerateConversations();
    Conversation LoadConversation(ConversationFile file);
    IReadOnlyDictionary<string, string> LoadSpeakerNames();
    void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot);
    (string ConversationsRoot, string StringTablesRoot) GetBackupRoots();
}
