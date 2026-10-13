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
