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

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ConversationItemViewModel MakeItem(string name)
    {
        var file = new ConversationFile(name, $@"quests\{name}.conversation", "quests",
                                        $@"quests\{name}.stringtable");
        return new ConversationItemViewModel(file);
    }
}
