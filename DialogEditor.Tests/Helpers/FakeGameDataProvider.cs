using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Helpers;

/// In-memory IGameDataProvider for tests. Only the members SampleProjectService and the
/// sample command touch are implemented; the rest throw so accidental use is obvious.
public sealed class FakeGameDataProvider : IGameDataProvider
{
    private readonly Dictionary<string, Conversation> _conversations;

    public FakeGameDataProvider(string gameId, string language, params Conversation[] conversations)
    {
        GameId = gameId;
        Language = language;
        _conversations = conversations.ToDictionary(c => c.Name);
    }

    public string GameName => "Fake";
    public string GameId   { get; }
    public IReadOnlyList<string> AvailableLanguages => [Language];
    public string Language { get; set; }

    public IReadOnlyList<ConversationFile> EnumerateConversations()
        => _conversations.Keys.Select(BuildNewConversationFile).ToList();

    public Conversation LoadConversation(ConversationFile file) => _conversations[file.Name];

    public ConversationFile BuildNewConversationFile(string name)
        => new(name, "conversations",
               $"conversations/{name}.conversation",
               $"stringtables/{name}.stringtable");

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
    public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) => throw new NotSupportedException();
    public string GetStringTablePath(ConversationFile file) => throw new NotSupportedException();
    public string GetStringTablePath(ConversationFile file, string language) => throw new NotSupportedException();
    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots() => throw new NotSupportedException();
    public void InitializeConversationFile(ConversationFile file) => throw new NotSupportedException();
}
