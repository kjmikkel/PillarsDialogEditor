using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Core.GameData;

public class Poe2GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity II: Deadfire";
    public string Language { get; set; } = "en";

    private string ExportedRoot    => Path.Combine(rootPath, "PillarsOfEternityII_Data", "exported");
    private string LocalizedRoot   => Path.Combine(ExportedRoot, "localized");
    private string ConversationsRoot => Path.Combine(ExportedRoot, "design", "conversations");
    private string StringTablesRoot  => Path.Combine(LocalizedRoot, Language, "text", "conversations");
    private string SpeakersBundle  => Path.Combine(ExportedRoot, "design", "gamedata", "speakers.gamedatabundle");

    public IReadOnlyList<string> AvailableLanguages =>
        Directory.Exists(LocalizedRoot)
            ? [.. Directory.GetDirectories(LocalizedRoot).Select(Path.GetFileName).Order()!]
            : ["en"];

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
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: string.Empty
        );
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe2ConversationParser.ParseFile(file.ConversationPath);
        var stPath = StringTablePathFor(file.ConversationPath);
        var strings = File.Exists(stPath)
            ? StringTableParser.ParseFile(stPath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }

    private string StringTablePathFor(string convPath)
    {
        var relative = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        return Path.Combine(StringTablesRoot, withoutExt + ".stringtable");
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        File.Exists(SpeakersBundle)
            ? Poe2SpeakerNameParser.ParseFile(SpeakersBundle)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
