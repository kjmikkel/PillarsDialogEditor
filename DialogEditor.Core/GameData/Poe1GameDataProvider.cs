using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Core.GameData;

public class Poe1GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity";
    public string GameId   => "poe1";
    public string Language { get; set; } = "en";

    private string DataRoot        => Path.Combine(rootPath, "PillarsOfEternity_Data", "data");
    private string LocalizedRoot   => Path.Combine(DataRoot, "localized");
    internal string ConversationsRoot => Path.Combine(DataRoot, "conversations");
    internal string StringTablesRoot  => Path.Combine(LocalizedRoot, Language, "text", "conversations");

    public IReadOnlyList<string> AvailableLanguages =>
        Directory.Exists(LocalizedRoot)
            ? [.. Directory.GetDirectories(LocalizedRoot).Select(Path.GetFileName).Order()!]
            : ["en"];

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
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: string.Empty
        );
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe1ConversationParser.ParseFile(file.ConversationPath);
        var stPath = StringTablePathFor(file.ConversationPath);
        var strings = File.Exists(stPath)
            ? StringTableParser.ParseFile(stPath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }

    public string GetStringTablePath(ConversationFile file)
        => StringTablePathFor(file.ConversationPath);

    private string StringTablePathFor(string convPath)
    {
        var relative = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        return Path.Combine(StringTablesRoot, withoutExt + ".stringtable");
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots()
        => (ConversationsRoot, StringTablesRoot);

    public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
    {
        Poe1ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
        var stPath = StringTablePathFor(file.ConversationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(stPath)!);
        StringTableSerializer.SaveToFile(stPath, snapshot.Nodes);
    }

    public ConversationFile BuildNewConversationFile(string name)
    {
        var convPath = Path.Combine(ConversationsRoot, name + ".conversation");
        var stPath   = Path.Combine(StringTablesRoot,  name + ".stringtable");
        return new ConversationFile(
            Name: name,
            FolderPath: string.Empty,
            ConversationPath: convPath,
            StringTablePath: stPath);
    }

    private const string Poe1BlankTemplate =
        """<?xml version="1.0" encoding="utf-8"?><ConversationData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema"><NextNodeID>0</NextNodeID><Nodes /></ConversationData>""";

    public void InitializeConversationFile(ConversationFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.ConversationPath)!);
        File.WriteAllText(file.ConversationPath, Poe1BlankTemplate, System.Text.Encoding.UTF8);
    }
}
