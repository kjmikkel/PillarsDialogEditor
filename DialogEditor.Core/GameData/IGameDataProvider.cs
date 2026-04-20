using DialogEditor.Core.Models;

namespace DialogEditor.Core.GameData;

public interface IGameDataProvider
{
    string GameName { get; }
    IReadOnlyList<ConversationFile> EnumerateConversations();
    Conversation LoadConversation(ConversationFile file);
}
