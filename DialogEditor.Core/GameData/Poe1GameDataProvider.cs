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

    private string CharactersStringtablePath =>
        Path.Combine(LocalizedRoot, Language, "text", "game", "characters.stringtable");

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        Poe1SpeakerNameParser.ParseFromDisk(ConversationsRoot, CharactersStringtablePath);

    private string GlobalVariablesPath =>
        Path.Combine(DataRoot, "design", "global", "game.globalvariables");

    public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
    {
        var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();

        // PoE1 conditions/scripts use only two lookup kinds: Speaker (served by
        // LoadSpeakerNames) and GlobalVariable (the Tag string stored in conditions).
        var vars = Poe1GlobalVariablesParser.ParseFile(GlobalVariablesPath);
        if (vars.Count > 0) result["GlobalVariable"] = vars;

        return result;
    }

    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots()
        => (ConversationsRoot, StringTablesRoot);

    public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
    {
        Poe1ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
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
