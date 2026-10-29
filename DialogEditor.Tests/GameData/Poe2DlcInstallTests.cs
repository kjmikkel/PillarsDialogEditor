using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

/// Issue 198 against a real PoE2 install (DIALOGEDITOR_POE2_DIR; docs/game-data-tests.md).
/// No fixed counts: the expectation is read from the install's own folders.
[Trait("Category", "GameData")]
public class Poe2DlcInstallTests
{
    private static Poe2GameDataProvider Provider() =>
        new(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable));

    // Every relative path is listed once, from the last data folder that has it: the game keeps
    // the last copy (the expansions each ship lax/lax_cv_craftsperson with the same ID).
    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void EveryRelativePath_IsListedOnce_FromTheLastFolderThatHasIt()
    {
        var dir = GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable);
        var lastCopy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var conversations in Poe2DataRoots.Existing(dir).Select(r => Path.Combine(r.Path, "design", "conversations")).Where(Directory.Exists))
            foreach (var path in Directory.EnumerateFiles(conversations, "*.conversationbundle", SearchOption.AllDirectories))
                lastCopy[Path.GetRelativePath(conversations, path)] = path;

        var files = Provider().EnumerateConversations();

        Assert.Equal(lastCopy.Values.Order(StringComparer.OrdinalIgnoreCase), files.Select(f => f.ConversationPath).Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(files.Count, files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // Opening the game folder loads every data folder's game data. Four shipped DLC bundles have
    // raw line breaks inside strings, which once failed the whole folder (issue 198).
    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void GameData_LoadsFromEveryDataFolder()
    {
        var provider = Provider();

        var names = provider.LoadGameDataNames();

        Assert.Equal(provider.EnumerateConversations().Count, names["Conversation"].Count);
        Assert.NotEmpty(names["Quest"]);
        Assert.NotEmpty(provider.LoadSpeakerNames());
    }

    // Issue 199: the global-variable suggestions are the game's declarations, every data
    // folder's *.globalvariablesbundle (the editor used to read a CSV no build ships).
    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void GlobalVariables_ListEveryDataFoldersDeclarations()
    {
        var dir = GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable);
        var dlcBundle = Poe2DataRoots.Existing(dir).Where(r => r.Key is not null)
            .Select(r => Path.Combine(r.Path, "design", "globalvariables"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.globalvariablesbundle"))
            .First();
        var dlcTag = DialogEditor.Core.Parsing.Poe2GlobalVariablesBundleParser.ParseFile(dlcBundle)[0].Name;

        var vars = Provider().LoadGameDataNames()["GlobalVariable"];

        Assert.Contains(vars, v => v.Name == dlcTag);
        Assert.Contains(vars, v => v.Name == "n_(NPC)_State");   // the base game's first declaration
    }

    // Issue 345: every offered global has a start-of-game value, and the shipped bundles set some
    // to non-zero.
    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void GlobalVariableInitialValues_CoverEveryOfferedGlobal()
    {
        var provider = Provider();

        var values = provider.LoadGlobalVariableInitialValues();

        Assert.Equal(provider.LoadGameDataNames()["GlobalVariable"].Select(v => v.Name).Order(StringComparer.Ordinal),
                     values.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(values.Values, v => v != 0);
    }

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void EveryDlcConversation_LoadsWithItsText()
    {
        var provider = Provider();
        var dlc = provider.EnumerateConversations().Where(f => f.DataRoot != ConversationFile.BaseDataRoot).ToList();
        Assert.NotEmpty(dlc);

        var withoutText = dlc
            .Where(f => File.Exists(provider.GetStringTablePath(f)))
            .Where(f => provider.LoadConversation(f).Strings.Count == 0)
            .Select(f => f.Name).ToList();
        Assert.True(withoutText.Count == 0, "DLC conversations whose text didn't load: " + string.Join(", ", withoutText));
    }
}
