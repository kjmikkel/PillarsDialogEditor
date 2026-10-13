using DialogEditor.Core.GameData;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.GameData;

/// Issue 201: PoE1's expansions keep their text in their own data folders (data_expansion1, …),
/// which the editor never read. The rules are the game's own StringTableManager: a table's
/// relative path is loaded from every folder, each line looked up from the last folder back to
/// the first, and a folder without the language falls back to its English table.
public class Poe1ExpansionTextTests : IDisposable
{
    private readonly FakePoe1Game _game = new();
    public void Dispose() => _game.Dispose();

    private string Folder(string dataFolder) => Path.Combine(_game.Root, "PillarsOfEternity_Data", dataFolder);

    private string TablePath(string dataFolder, string lang, string relative) =>
        Path.Combine(Folder(dataFolder), "localized", lang, "text", relative.Replace('/', Path.DirectorySeparatorChar) + ".stringtable");

    private string Table(string dataFolder, string lang, string relative, params (int Id, string Text)[] lines)
    {
        var path = TablePath(dataFolder, lang, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<StringTableFile><Entries>" +
            string.Concat(lines.Select(l => $"<Entry><ID>{l.Id}</ID><DefaultText>{l.Text}</DefaultText><FemaleText></FemaleText></Entry>")) +
            "</Entries></StringTableFile>");
        return path;
    }

    private const string Canonical = "conversations/canonical/canonical";

    [Fact]
    public void Text_IsMergedPerLine_TheLaterFolderWinning()
    {
        Table("data_expansion1", "en", Canonical, (0, "expansion1 line"), (99, "added in 1"));
        Table("data_expansion2", "en", Canonical, (99, "added in 2"));
        var provider = _game.Provider;

        var strings = provider.LoadConversation(_game.File).Strings;

        Assert.Equal("expansion1 line", strings.Get(0)!.DefaultText);
        Assert.Equal("added in 2", strings.Get(99)!.DefaultText);
        Assert.NotNull(strings.Get(1));   // base lines the expansions don't have stay
    }

    [Fact]
    public void AFolderWithoutTheLanguage_UsesItsEnglishTable()
    {
        Table("data_expansion1", "en", Canonical, (0, "expansion1 english"));
        var provider = _game.Provider;
        provider.Language = "de";

        var strings = provider.LoadConversation(_game.File).Strings;

        Assert.Equal("expansion1 english", strings.Get(0)!.DefaultText);
        Assert.NotEqual("expansion1 english", strings.Get(1)!.DefaultText);
    }

    [Fact]
    public void TheBackerBetaFolder_IsSkipped_AsTheGameDoes()
    {
        Table("data_backerbeta", "en", Canonical, (0, "backer beta"));

        var strings = _game.Provider.LoadConversation(_game.File).Strings;

        Assert.NotEqual("backer beta", strings.Get(0)!.DefaultText);
    }

    [Fact]
    public void TextIsWritten_ToTheLastFolderWithATable_InTheChosenLanguage()
    {
        Table("data_expansion1", "en", Canonical, (0, "expansion1 english"));   // no "de" table there
        var provider = _game.Provider;

        Assert.Equal(TablePath("data_expansion1", "en", Canonical), provider.GetStringTablePath(_game.File, "en"));
        // The game shows expansion1's English line in German too, so a German edit belongs beside it.
        Assert.Equal(TablePath("data_expansion1", "de", Canonical), provider.GetStringTablePath(_game.File, "de"));
    }

    [Fact]
    public void WithNoExpansionTable_TextIsWrittenToTheBaseFolder_AsBefore()
    {
        Directory.CreateDirectory(Path.Combine(Folder("data_expansion1"), "localized", "en", "text", "conversations"));

        Assert.Equal(_game.StPath("de"), _game.Provider.GetStringTablePath(_game.File, "de"));
    }

    [Fact]
    public void SpeakerNames_UseTheExpansionsCharacterNames()
    {
        var conv = Path.Combine(_game.DataRoot, "conversations", "px1", "px1_speaker.conversation");
        Directory.CreateDirectory(Path.GetDirectoryName(conv)!);
        File.WriteAllText(conv, """
            <?xml version="1.0" encoding="utf-8"?>
            <ConversationData><CharacterMappings><CharacterMapping>
              <Guid>aaaaaaaa-0000-0000-0000-000000000001</Guid><InstanceTag>PX1_NPC_lady_webb</InstanceTag>
            </CharacterMapping></CharacterMappings><Nodes /></ConversationData>
            """);
        Table("data_expansion1", "en", "game/characters", (1, "Lady Webb"));

        var names = _game.Provider.LoadSpeakerNames();

        Assert.Equal("Lady Webb", names["aaaaaaaa-0000-0000-0000-000000000001"]);
    }
}
