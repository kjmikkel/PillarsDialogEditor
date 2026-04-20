using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Core.GameData;

public class Poe1GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity";

    private string DataRoot => Path.Combine(rootPath, "PillarsOfEternity_Data", "data");
    private string ConversationsRoot => Path.Combine(DataRoot, "conversations");
    private string StringTablesRoot => Path.Combine(DataRoot, "localized", "en", "text", "conversations");

    public IReadOnlyList<ConversationFile> EnumerateConversations()
    {
        if (!Directory.Exists(ConversationsRoot)) return [];

        return Directory
            .EnumerateFiles(ConversationsRoot, "*.conversation", SearchOption.AllDirectories)
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
        var nodes = Poe1ConversationParser.ParseFile(file.ConversationPath);
        var strings = File.Exists(file.StringTablePath)
            ? StringTableParser.ParseFile(file.StringTablePath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }
}
