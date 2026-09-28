using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.GameData;

/// One unreadable stringtable must not make its conversation unopenable, and must never be
/// overwritten (issue #119). A GOG Deadfire install has a German stringtable that is binary
/// data, not XML (05_bs_construct); these tests use the first bytes of that file.
public class UnreadableStringTableTests : IDisposable
{
    // The start of the real damaged file: not XML at all.
    private static readonly byte[] Garbage =
        [0x2C, 0x56, 0x00, 0x1F, 0x2A, 0xD9, 0x25, 0x06, 0x06, 0xA8, 0x1D, 0x0F, 0x1D, 0x00, 0x01, 0x22];

    private readonly FakePoe2Game _game = new("en", "de");

    public UnreadableStringTableTests() => File.WriteAllBytes(_game.StPath("de"), Garbage);

    public void Dispose() => _game.Dispose();

    private static ConversationPatch PatchWithText(string conv) =>
        new(conv, ConversationPatch.CurrentSchemaVersion,
            [new NodeEditSnapshot(99, false, SpeakerCategory.Npc, "spk", "lst", "", "",
                "Conversation", "None", "", "", "", false, false, [], [], [])], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(99, "added line", "")],
                ["de"] = [new NodeTranslation(99, "neue Zeile", "")],
            },
        };

    // ── Reading ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadFile_NotXml_IsAnEmptyUnreadableTable()
    {
        var table = StringTableParser.LoadFile(_game.StPath("de"));

        Assert.True(table.IsUnreadable);
        Assert.True(table.IsMissing);   // shown with the existing missing-text placeholder
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void LoadFile_Absent_IsMissingButNotUnreadable()
    {
        var table = StringTableParser.LoadFile(Path.Combine(_game.Root, "nope.stringtable"));

        Assert.True(table.IsMissing);
        Assert.False(table.IsUnreadable);
    }

    [Fact]
    public void LoadFile_Valid_IsReadable()
    {
        var table = StringTableParser.LoadFile(_game.StPath("en"));

        Assert.False(table.IsMissing);
        Assert.False(table.IsUnreadable);
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void LoadConversation_InTheDamagedLanguage_LoadsTheStructure()
    {
        var provider = _game.Provider;
        provider.Language = "de";

        var conversation = provider.LoadConversation(provider.FindConversation("test_conv")!);

        Assert.NotEmpty(conversation.Nodes);
        Assert.True(conversation.Strings.IsUnreadable);
    }

    // ── Writing ──────────────────────────────────────────────────────────

    [Fact]
    public void SaveToFile_Unreadable_RefusesAndLeavesTheFileUntouched()
    {
        var path = _game.StPath("de");

        Assert.Throws<StringTableUnreadableException>(() =>
            StringTableSerializer.SaveToFile(path, [new NodeTranslation(1, "x", "")]));

        Assert.Equal(Garbage, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void WriteTranslations_SkipsTheUnreadableLanguage_AndWritesTheOthers()
    {
        var provider = _game.Provider;
        var file     = provider.FindConversation("test_conv")!;

        var skipped = TranslationApplier.WriteTranslations(file, PatchWithText("test_conv"), provider);

        Assert.Equal([_game.StPath("de")], skipped);
        Assert.Equal(Garbage, File.ReadAllBytes(_game.StPath("de")));
        Assert.Contains("added line", File.ReadAllText(_game.StPath("en")));
    }

    [Fact]
    public void PatcherInstall_ReportsTheUnreadableStringTable_AndStillApplies()
    {
        var result = PatchInstaller.Install(_game.Provider, _game.Root,
            [new InstallEntry(DialogProject.Empty("m").WithPatch(PatchWithText("test_conv")))],
            new InstallOptions());

        var applied = Assert.IsType<InstallResult.Applied>(result);
        Assert.Equal(1, applied.ConversationsPatched);
        Assert.Equal([_game.StPath("de")], applied.UnreadableStringTables);
        Assert.Equal(Garbage, File.ReadAllBytes(_game.StPath("de")));
    }
}
