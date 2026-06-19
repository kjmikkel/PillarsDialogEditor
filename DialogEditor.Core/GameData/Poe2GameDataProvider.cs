using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Core.GameData;

public class Poe2GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity II: Deadfire";
    public string GameId   => "poe2";
    public string Language { get; set; } = "en";

    private string ExportedRoot    => Path.Combine(rootPath, "PillarsOfEternityII_Data", "exported");
    private string LocalizedRoot   => Path.Combine(ExportedRoot, "localized");
    internal string ConversationsRoot => Path.Combine(ExportedRoot, "design", "conversations");
    internal string StringTablesRoot  => Path.Combine(LocalizedRoot, Language, "text", "conversations");
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
        var stPath = StringTablePathFor(file.ConversationPath, Language);
        var strings = File.Exists(stPath)
            ? StringTableParser.ParseFile(stPath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }

    public string GetStringTablePath(ConversationFile file)
        => StringTablePathFor(file.ConversationPath, Language);

    public string GetStringTablePath(ConversationFile file, string language)
        => StringTablePathFor(file.ConversationPath, language);

    private string StringTablePathFor(string convPath, string language)
    {
        var relative   = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        var root       = Path.Combine(LocalizedRoot, language, "text", "conversations");
        return Path.Combine(root, withoutExt + ".stringtable");
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        File.Exists(SpeakersBundle)
            ? Poe2SpeakerNameParser.ParseFile(SpeakersBundle)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Stub: real implementation will parse gamedatabundle files per LookupKind.
    public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
        => new Dictionary<string, IReadOnlyList<GameDataEntry>>();

    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots()
        => (ConversationsRoot, StringTablesRoot);

    public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
    {
        Poe2ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
    }

    public ConversationFile BuildNewConversationFile(string name)
    {
        var convPath = Path.Combine(ConversationsRoot, name + ".conversationbundle");
        var stPath   = Path.Combine(StringTablesRoot,  name + ".stringtable");
        return new ConversationFile(
            Name: name,
            FolderPath: string.Empty,
            ConversationPath: convPath,
            StringTablePath: stPath);
    }

    private const string Poe2BlankTemplate =
        """{"Conversations":[{"Nodes":[]}]}""";

    public void InitializeConversationFile(ConversationFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.ConversationPath)!);
        File.WriteAllText(file.ConversationPath, Poe2BlankTemplate, System.Text.Encoding.UTF8);
    }
}
