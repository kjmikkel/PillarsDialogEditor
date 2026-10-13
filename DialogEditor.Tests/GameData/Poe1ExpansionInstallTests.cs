using DialogEditor.Core.GameData;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.GameData;

/// Issue 201 against a real PoE1 install (DIALOGEDITOR_POE1_DIR; docs/game-data-tests.md).
/// No fixed counts: the expectation is read from the install's own folders.
[Trait("Category", "GameData")]
public class Poe1ExpansionInstallTests
{
    private static string Dir => GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable);

    // Every conversation with text in an expansion folder, with that folder's English table (the
    // last one when both expansions have one), which the game reads first for each line in it.
    private static IEnumerable<(ConversationFile File, string Table)> WithExpansionText(Poe1GameDataProvider provider)
    {
        var conversations = Path.Combine(Dir, Poe1DataRoots.DataFolder, Poe1DataRoots.Base, "conversations");
        var expansions = Poe1DataRoots.Existing(Dir).Where(r => r.Folder != Poe1DataRoots.Base).Reverse().ToList();
        foreach (var file in provider.EnumerateConversations())
        {
            var relative = Path.ChangeExtension(Path.GetRelativePath(conversations, file.ConversationPath), ".stringtable");
            var table = expansions.Select(r => Path.Combine(r.Path, "localized", "en", "text", "conversations", relative))
                                  .FirstOrDefault(File.Exists);
            if (table is not null) yield return (file, table);
        }
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void EveryConversationWithExpansionText_LoadsIt_TheExpansionsLinesWinning()
    {
        var provider = new Poe1GameDataProvider(Dir);
        var cases = WithExpansionText(provider).ToList();
        Assert.NotEmpty(cases);

        var wrong = new List<string>();
        foreach (var (file, table) in cases)
        {
            var loaded = provider.LoadConversation(file).Strings;
            if (StringTableParser.LoadFile(table).Entries.Any(e => loaded.Get(e.Id)?.DefaultText != e.DefaultText))
                wrong.Add(file.Name);
        }
        Assert.True(wrong.Count == 0, "Conversations not showing their expansion text: " + string.Join(", ", wrong));
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void ExpansionText_IsWrittenToTheExpansionTable()
    {
        var provider = new Poe1GameDataProvider(Dir);

        var wrong = WithExpansionText(provider).Where(c => provider.GetStringTablePath(c.File, "en") != c.Table)
                                               .Select(c => c.File.Name).ToList();

        Assert.True(wrong.Count == 0, "Written elsewhere: " + string.Join(", ", wrong));
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void TheBackup_KeepsEachExpansionsText()
    {
        var folders = ((IGameDataProvider)new Poe1GameDataProvider(Dir)).GetExtraBackupFolders();

        Assert.Contains(folders, f => f.SnapshotPath == "dlc/data_expansion1/stringtables/en");
        Assert.All(folders, f => Assert.True(Directory.Exists(f.LivePath)));
    }
}
