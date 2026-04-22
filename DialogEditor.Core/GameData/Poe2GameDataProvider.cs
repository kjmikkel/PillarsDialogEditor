using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Core.GameData;

public class Poe2GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity II: Deadfire";

    private string ExportedRoot => Path.Combine(rootPath, "PillarsOfEternityII_Data", "exported");
    private string ConversationsRoot => Path.Combine(ExportedRoot, "design", "conversations");
    private string StringTablesRoot => Path.Combine(ExportedRoot, "localized", "en", "text", "conversations");
    private string SpeakersBundle => Path.Combine(ExportedRoot, "design", "gamedata", "speakers.gamedatabundle");

    public IReadOnlyList<ConversationFile> EnumerateConversations()
    {
        if (!Directory.Exists(ConversationsRoot)) return [];

        return Directory
            .EnumerateFiles(ConversationsRoot, "*.conversationbundle", SearchOption.AllDirectories)
            .Select(BuildConversationFile)
            .OrderBy(f => f.FolderPath)
            .ThenBy(f => f.Name)
            .ToList();
    }

    private ConversationFile BuildConversationFile(string convPath)
    {
        var relative = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        var stPath = Path.Combine(StringTablesRoot, withoutExt + ".stringtable");
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: stPath
        );
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe2ConversationParser.ParseFile(file.ConversationPath);
        var strings = File.Exists(file.StringTablePath)
            ? StringTableParser.ParseFile(file.StringTablePath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        File.Exists(SpeakersBundle)
            ? Poe2SpeakerNameParser.ParseFile(SpeakersBundle)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
