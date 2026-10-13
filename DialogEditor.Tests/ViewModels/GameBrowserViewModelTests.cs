using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class GameBrowserViewModelTests
{
    public GameBrowserViewModelTests() => Loc.Configure(new StubStringProvider());

    private static GameBrowserViewModel MakeVm() =>
        new(new StubDispatcher());

    // ── FilteredFolders — no filter: returns all ──────────────────────────

    [Fact]
    public void FilteredFolders_EmptyFilter_ReturnsAllFolders()
    {
        var vm = MakeVm();
        vm.Folders.Add(new ConversationFolderViewModel("FolderA"));
        vm.Folders.Add(new ConversationFolderViewModel("FolderB"));
        Assert.Equal(2, vm.FilteredFolders.Count());
    }

    // ── FilteredFolders — with filter ─────────────────────────────────────

    [Fact]
    public void FilteredFolders_WithFilter_OnlyMatchingConversations()
    {
        var vm = MakeVm();
        var folder = new ConversationFolderViewModel("quests");
        folder.Items.Add(MakeItem("quest_main"));
        folder.Items.Add(MakeItem("intro_scene"));
        vm.Folders.Add(folder);

        vm.FilterText = "quest";

        var filtered = vm.FilteredFolders.ToList();
        Assert.Single(filtered);
        Assert.Single(filtered[0].Items);
        Assert.Equal("quest_main", filtered[0].Items[0].Name);
    }

    [Fact]
    public void FilteredFolders_WithFilter_CaseInsensitive()
    {
        var vm = MakeVm();
        var folder = new ConversationFolderViewModel("all");
        folder.Items.Add(MakeItem("IntroScene"));
        vm.Folders.Add(folder);
        vm.FilterText = "intro";
        Assert.Single(vm.FilteredFolders);
    }

    [Fact]
    public void FilteredFolders_NoMatchingItems_ExcludesFolder()
    {
        var vm = MakeVm();
        var folder = new ConversationFolderViewModel("all");
        folder.Items.Add(MakeItem("intro_scene"));
        vm.Folders.Add(folder);
        vm.FilterText = "boss_fight";
        Assert.Empty(vm.FilteredFolders);
    }

    // ── ClearFilter ───────────────────────────────────────────────────────

    [Fact]
    public void ClearFilter_SetsFilterTextToEmpty()
    {
        var vm = MakeVm();
        vm.FilterText = "quest";
        vm.ClearFilterCommand.Execute(null);
        Assert.Equal(string.Empty, vm.FilterText);
    }

    [Fact]
    public void ClearFilterCommand_CanExecute_OnlyWhenFilterTextIsSet()
    {
        var vm = MakeVm();
        vm.FilterText = string.Empty;
        Assert.False(vm.ClearFilterCommand.CanExecute(null));
        vm.FilterText = "quest";
        Assert.True(vm.ClearFilterCommand.CanExecute(null));
    }

    // ── ConversationSelected event ────────────────────────────────────────

    [Fact]
    public void SelectedTreeItem_WhenConversationItem_FiresConversationSelected()
    {
        var vm        = MakeVm();
        var folder    = new ConversationFolderViewModel("quests");
        var item      = MakeItem("quest_main");
        folder.Items.Add(item);
        vm.Folders.Add(folder);

        ConversationFile? selected = null;
        vm.ConversationSelected += f => selected = f;

        vm.SelectedTreeItem = item;

        Assert.NotNull(selected);
        Assert.Equal("quest_main", selected.Name);
    }

    [Fact]
    public void SelectedTreeItem_WhenFolder_DoesNotFireConversationSelected()
    {
        var vm     = MakeVm();
        var folder = new ConversationFolderViewModel("quests");
        vm.Folders.Add(folder);

        var fired = false;
        vm.ConversationSelected += _ => fired = true;
        vm.SelectedTreeItem = folder;
        Assert.False(fired);
    }

    // ── ExpandAll / CollapseAll ───────────────────────────────────────────

    [Fact]
    public async Task ExpandAll_SetsAllFoldersExpanded()
    {
        var vm = MakeVm();
        vm.Folders.Add(new ConversationFolderViewModel("A", isExpanded: false));
        vm.Folders.Add(new ConversationFolderViewModel("B", isExpanded: false));
        await vm.ExpandAllCommand.ExecuteAsync(null);
        Assert.All(vm.Folders, f => Assert.True(f.IsExpanded));
    }

    [Fact]
    public async Task CollapseAll_SetsAllFoldersCollapsed()
    {
        var vm = MakeVm();
        vm.Folders.Add(new ConversationFolderViewModel("A", isExpanded: true));
        vm.Folders.Add(new ConversationFolderViewModel("B", isExpanded: true));
        await vm.CollapseAllCommand.ExecuteAsync(null);
        Assert.All(vm.Folders, f => Assert.False(f.IsExpanded));
    }

    // ── Load — new-conversation grouping ─────────────────────────────────

    [Fact]
    public void Load_WithNewConversationNames_InsertsDedicatedFolderAtTop()
    {
        var vm       = MakeVm();
        var provider = new FakeGameDataProvider("test-game", "en");

        vm.Load(provider, ["new_scene"]);

        var newFolder = vm.Folders[0];
        Assert.Equal(Loc.Get("Label_NewConversationsFolder"), newFolder.DisplayName);
        Assert.True(newFolder.IsExpanded);
        Assert.Single(newFolder.Items);
        Assert.True(newFolder.Items[0].IsNew);
        Assert.Equal("new_scene", newFolder.Items[0].Name);
    }

    [Fact]
    public void Load_WithoutNewConversationNames_DoesNotAddNewFolder()
    {
        var vm       = MakeVm();
        var provider = new FakeGameDataProvider("test-game", "en");

        vm.Load(provider);

        Assert.DoesNotContain(vm.Folders, f => f.DisplayName == Loc.Get("Label_NewConversationsFolder"));
    }

    // ── Nested tree (issue 85, part B) ──────────────────────────────────

    private static GameBrowserViewModel VmWith(params ConversationFile[] files)
    {
        var vm = MakeVm();
        var tree = ConversationTreeBuilder.Build(files);
        foreach (var f in tree.Folders) vm.Folders.Add(f);
        foreach (var i in tree.RootItems) vm.RootItems.Add(i);
        return vm;
    }

    private static ConversationFile FileIn(string folder, string name) =>
        new(name, folder, $@"{folder}\{name}.conversation", $@"{folder}\{name}.stringtable");

    [Fact]
    public void Filter_KeepsANestedMatch_InsideItsExpandedAncestors()
    {
        var vm = VmWith(FileIn("re_scripted_interactions", "other"),
                        FileIn(@"re_scripted_interactions\bounty encounters", "bounty_hunter"));
        vm.FilterText = "hunter";

        var top = Assert.Single(vm.FilteredFolders);
        Assert.True(top.IsExpanded);
        Assert.Empty(top.Items);
        var nested = Assert.Single(top.Subfolders);
        Assert.True(nested.IsExpanded);
        Assert.Equal("bounty_hunter", Assert.Single(nested.Items).Name);
    }

    [Fact]
    public void Filter_KeepsAFoldersNameAndTooltip()
    {
        // Issue 85, part D: the prototype group explains itself in its tooltip; a filtered copy
        // of it must not fall back to its virtual path.
        var vm = VmWith(FileIn("", "si_test"));
        vm.FilterText = "si_";

        var group = Assert.Single(vm.FilteredFolders);
        Assert.Equal(vm.Folders[0].DisplayName, group.DisplayName);
        Assert.Equal(vm.Folders[0].ToolTip, group.ToolTip);
    }

    [Fact]
    public void FilteredRoots_ListsFoldersThenTopLevelConversations()
    {
        var vm = VmWith(FileIn("", "gather_your_party"), FileIn("companions", "eder"));
        var roots = vm.FilteredRoots.ToList();
        Assert.IsType<ConversationFolderViewModel>(roots[0]);
        Assert.Equal("gather_your_party", Assert.IsType<ConversationItemViewModel>(roots[1]).Name);

        vm.FilterText = "eder";
        Assert.IsType<ConversationFolderViewModel>(Assert.Single(vm.FilteredRoots));
        vm.FilterText = "party";
        Assert.Equal("gather_your_party", Assert.IsType<ConversationItemViewModel>(Assert.Single(vm.FilteredRoots)).Name);
    }

    [Fact]
    public async Task ExpandAll_AndCollapseAll_ReachNestedFolders()
    {
        var vm = VmWith(FileIn(@"companions\poi", "x"));
        var nested = vm.Folders[0].Subfolders[0];

        await vm.ExpandAllCommand.ExecuteAsync(null);
        Assert.True(nested.IsExpanded);
        await vm.CollapseAllCommand.ExecuteAsync(null);
        Assert.False(nested.IsExpanded);
    }

    [Fact]
    public void Load_BuildsTheTreeWithTidiedFolderNames()
    {
        // FakeGameDataProvider files every conversation under "conversations".
        var vm = MakeVm();
        vm.Load(new FakeGameDataProvider("test-game", "en", new Conversation("scene", [], new StringTable([]))));
        var folder = Assert.Single(vm.Folders);
        Assert.Equal("conversations", folder.FolderPath);
        Assert.Equal("Conversations", folder.DisplayName);
        Assert.Empty(vm.RootItems);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ConversationItemViewModel MakeItem(string name)
    {
        var file = new ConversationFile(name, $@"quests\{name}.conversation", "quests",
                                        $@"quests\{name}.stringtable");
        return new ConversationItemViewModel(file);
    }
}
