using DialogEditor.Core.GameData;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// Issue 85, part B (docs/superpowers/specs/2026-10-13-conversations-dock-b-names-design.md):
/// the Conversations dock shows real nested folders with tidied names, the game's top-level
/// conversations at the top level, and PoE1's White March folders grouped.
/// </summary>
public class FolderDisplayNameTests
{
    [Theory]
    [InlineData("07_neketaka_temple_district", "Neketaka Temple District")]
    [InlineData("00_prototype", "Prototype")]
    [InlineData("companions", "Companions")]
    [InlineData("poi", "Poi")]
    [InlineData("bounty encounters", "Bounty Encounters")]
    [InlineData("prototype_2", "Prototype 2")]
    [InlineData("lax02_01_iceberg_dungeon", "Lax02 01 Iceberg Dungeon")]
    [InlineData("px4", "Px4")]
    [InlineData("10_od_nua", "Od Nua")]
    public void TidiesAFolderName(string raw, string expected)
        => Assert.Equal(expected, FolderDisplayName.For(raw));

    [Theory]
    // Nothing left to show after the prefix: keep the raw name rather than an empty label.
    [InlineData("42", "42")]
    [InlineData("07_", "07_")]
    [InlineData("", "")]
    public void KeepsTheRawName_WhenTidyingLeavesNothing(string raw, string expected)
        => Assert.Equal(expected, FolderDisplayName.For(raw));
}

public class ConversationTreeBuilderTests
{
    public ConversationTreeBuilderTests() => Loc.Configure(new StubStringProvider());

    private static ConversationFile File(string folder, string name) =>
        new(name, folder, $@"{folder}\{name}.conversation", $@"{folder}\{name}.stringtable");

    private static ConversationTree Build(params ConversationFile[] files) => ConversationTreeBuilder.Build(files);

    [Fact]
    public void NestedFolders_GoInsideTheirParent()
    {
        var tree = Build(
            File("07_neketaka_temple_district", "a"),
            File(@"07_neketaka_temple_district\07_animancer_one_offs", "b"));

        var parent = Assert.Single(tree.Folders);
        Assert.Equal("Neketaka Temple District", parent.DisplayName);
        Assert.Equal("a", Assert.Single(parent.Items).Name);
        var child = Assert.Single(parent.Subfolders);
        Assert.Equal("Animancer One Offs", child.DisplayName);
        Assert.Equal(@"07_neketaka_temple_district\07_animancer_one_offs", child.FolderPath);
        Assert.Equal("b", Assert.Single(child.Items).Name);
    }

    [Fact]
    public void AMissingParent_IsCreated()
    {
        var tree = Build(File(@"re_scripted_interactions\bounty encounters", "x"));

        var parent = Assert.Single(tree.Folders);
        Assert.Equal("re_scripted_interactions", parent.FolderPath);
        Assert.Empty(parent.Items);
        Assert.Equal("Bounty Encounters", Assert.Single(parent.Subfolders).DisplayName);
    }

    [Fact]
    public void ForwardSlashes_NestToo()
    {
        var tree = Build(File("companions/poi", "x"));
        Assert.Equal("Poi", Assert.Single(Assert.Single(tree.Folders).Subfolders).DisplayName);
    }

    [Fact]
    public void Folders_SortByRawName_SoTheGamesNumberingStillOrdersThem()
    {
        var tree = Build(File("companions", "c"), File("10_magrans_teeth", "b"), File("02_poko_kohara", "a"));
        Assert.Equal(["02_poko_kohara", "10_magrans_teeth", "companions"], tree.Folders.Select(f => f.FolderPath));
    }

    [Fact]
    public void Children_ListSubfoldersBeforeConversations()
    {
        var tree = Build(File("companions", "eder"), File(@"companions\poi", "x"));
        var companions = Assert.Single(tree.Folders);
        Assert.IsType<ConversationFolderViewModel>(companions.Children.First());
        Assert.IsType<ConversationItemViewModel>(companions.Children.Last());
    }

    [Fact]
    public void TopLevelConversations_AreNotPutInAFolder()
    {
        var tree = Build(File("", "gather_your_party"), File("companions", "eder"));
        Assert.Equal("gather_your_party", Assert.Single(tree.RootItems).Name);
        Assert.Equal("companions", Assert.Single(tree.Folders).FolderPath);
    }

    [Fact]
    public void WhiteMarchFolders_AreGroupedAfterTheBaseGame()
    {
        var tree = Build(
            File("px1_00_stalwart_village", "a"),
            File(@"px1_companions\poi", "b"),
            File("px2_03_abbey", "c"),
            File("00_dyrwood", "d"),
            File("companions", "e"));

        Assert.Equal(
            ["00_dyrwood", "companions", ConversationTreeBuilder.WhiteMarch1Path, ConversationTreeBuilder.WhiteMarch2Path],
            tree.Folders.Select(f => f.FolderPath));

        var part1 = tree.Folders[2];
        Assert.Equal("Browser_Dlc_WhiteMarch1", part1.DisplayName); // the stub returns the key
        Assert.Equal(["Stalwart Village", "Companions"], part1.Subfolders.Select(f => f.DisplayName));
        Assert.Equal("px1_00_stalwart_village", part1.Subfolders[0].FolderPath);
        Assert.Equal("Poi", Assert.Single(part1.Subfolders[1].Subfolders).DisplayName);

        var part2 = tree.Folders[3];
        Assert.Equal("Browser_Dlc_WhiteMarch2", part2.DisplayName);
        Assert.Equal("Abbey", Assert.Single(part2.Subfolders).DisplayName);
    }

    [Theory]
    // Prefixes that can't be mapped to a named DLC with confidence are only tidied.
    [InlineData("lax02_01_iceberg_dungeon")]
    [InlineData("px4")]
    [InlineData("laxi")]
    public void OtherPrefixes_AreNotGrouped(string folder)
    {
        var tree = Build(File(folder, "x"));
        Assert.Equal(folder, Assert.Single(tree.Folders).FolderPath);
    }
}
