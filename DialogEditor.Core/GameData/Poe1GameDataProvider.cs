using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Resources;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Core.GameData;

public class Poe1GameDataProvider(string rootPath) : IGameDataProvider
{
    // The shipped games are themselves localised, so their titles are UI copy
    // rather than identifiers — GameId below is the stable technical handle.
    public string GameName => CoreStrings.Game_Poe1_Name;
    public string GameId   => "poe1";
    public string Language { get; set; } = "en";

    private string DataRoot        => Path.Combine(rootPath, "PillarsOfEternity_Data", "data");
    private string LocalizedRoot   => Path.Combine(DataRoot, "localized");
    internal string ConversationsRoot => Path.Combine(DataRoot, "conversations");
    internal string StringTablesRoot  => GetStringTablesRoot(Language);

    public string GetStringTablesRoot(string language) =>
        Path.Combine(LocalizedRoot, language, "text", "conversations");

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
        return new Conversation(file.Name, nodes, LoadText(file, Language));
    }

    public string GetStringTablePath(ConversationFile file)
        => StringTablePathFor(file, Language);

    public string GetStringTablePath(ConversationFile file, string language)
        => StringTablePathFor(file, language);

    private IReadOnlyList<Poe1DataRoot> Roots => Poe1DataRoots.Existing(rootPath);

    // One text table per data folder that has one, in the game's folder order, with the folder it
    // is in. A folder without the language falls back to its English table, as the game's
    // StringTableManager does per folder (issue 201).
    private IReadOnlyList<(Poe1DataRoot Root, string Path)> TablesFor(string relative, string language) =>
        Roots.Select(r => (Root: r, Path: new[] { language, "en" }.Distinct()
                 .Select(lang => Path.Combine(r.Path, "localized", lang, "text", relative))
                 .FirstOrDefault(File.Exists)))
             .Where(t => t.Path is not null)
             .Select(t => (t.Root, t.Path!))
             .ToList();

    private string TextRelativePath(ConversationFile file) =>
        Path.Combine("conversations", Path.ChangeExtension(Path.GetRelativePath(ConversationsRoot, file.ConversationPath), null) + ".stringtable");

    // The White March's text is in data_expansion1 and _2, beside the base game's lines it
    // replaces; the game looks each line up from the last folder back (issue 201).
    private StringTable LoadText(ConversationFile file, string language) =>
        StringTable.MergeLaterWins(TablesFor(TextRelativePath(file), language).Select(t => StringTableParser.LoadFile(t.Path)).ToList());

    // Written to the last folder that has a table for the conversation, which the game reads first
    // for every line it holds, in the chosen language even when that folder only had an English
    // table: the game would show that English line, so the edit belongs beside it. With no table
    // anywhere, the base game's folder, as before (issue 201).
    private string StringTablePathFor(ConversationFile file, string language)
    {
        var relative = TextRelativePath(file);
        var folder = TablesFor(relative, language) is { Count: > 0 } tables ? tables[^1].Root.Path : DataRoot;
        return Path.Combine(folder, "localized", language, "text", relative);
    }

    // Every folder's character names (The White March's are in the expansions), so a speaker tag
    // resolves to the name the game shows (issue 201).
    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        Poe1SpeakerNameParser.ParseFromDisk(ConversationsRoot,
            TablesFor(Path.Combine("game", "characters.stringtable"), Language).Select(t => t.Path).ToList());

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

    public IReadOnlyDictionary<string, int> LoadGlobalVariableInitialValues() =>
        Poe1GlobalVariablesParser.ParseInitialValuesFile(GlobalVariablesPath);

    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots()
        => (ConversationsRoot, StringTablesRoot);

    // Each expansion folder's conversation text per installed language (issue 201). Its
    // conversations are in data, which the base backup already keeps.
    public IReadOnlyList<ExtraBackupFolder> GetExtraBackupFolders() =>
        Roots.Where(r => r.Folder != Poe1DataRoots.Base).SelectMany(r => AvailableLanguages
            .Select(lang => (Lang: lang, Text: Path.Combine(r.Path, "localized", lang, "text", "conversations")))
            .Where(t => Directory.Exists(t.Text))
            .Select(t => new ExtraBackupFolder($"dlc/{r.Folder}/stringtables/{t.Lang}", t.Text, SinceFormat: 4)))
        .ToList();

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
