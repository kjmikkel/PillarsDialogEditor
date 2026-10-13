using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// Issue 85, part C (docs/superpowers/specs/2026-10-13-conversations-dock-c-project-design.md):
/// the Conversations dock marks the conversations the open project has changed, and can narrow
/// the tree to them.
/// </summary>
public class GameBrowserProjectMarksTests
{
    public GameBrowserProjectMarksTests() => Loc.Configure(new StubStringProvider());

    private static ConversationFile FileIn(string folder, string name) =>
        new(name, folder, $@"{folder}\{name}.conversation", $@"{folder}\{name}.stringtable");

    private static GameBrowserViewModel Browser()
    {
        var vm = new GameBrowserViewModel(new StubDispatcher());
        var tree = ConversationTreeBuilder.Build(
        [
            FileIn("companions", "eder"),
            FileIn(@"companions\poi", "eder_poi"),
            FileIn("companions", "aloth"),
            FileIn("", "gather_your_party"),
        ]);
        foreach (var f in tree.Folders) vm.Folders.Add(f);
        foreach (var i in tree.RootItems) vm.RootItems.Add(i);
        return vm;
    }

    private static IEnumerable<ConversationItemViewModel> AllItems(GameBrowserViewModel vm)
    {
        IEnumerable<ConversationItemViewModel> In(ConversationFolderViewModel f) =>
            f.Items.Concat(f.Subfolders.SelectMany(In));
        return vm.Folders.SelectMany(In).Concat(vm.RootItems);
    }

    private static ConversationItemViewModel Item(GameBrowserViewModel vm, string name) =>
        AllItems(vm).Single(i => i.Name == name);

    [Fact]
    public void SetProjectConversations_MarksExactlyTheNamedConversations_AtAnyDepth()
    {
        var vm = Browser();
        vm.SetProjectConversations(new HashSet<string> { "eder_poi", "gather_your_party" });

        Assert.True(Item(vm, "eder_poi").IsEdited);
        Assert.True(Item(vm, "gather_your_party").IsEdited);
        Assert.False(Item(vm, "eder").IsEdited);
        Assert.False(Item(vm, "aloth").IsEdited);
        Assert.True(vm.HasProject);
    }

    [Fact]
    public void SetProjectConversations_ALaterCall_ReplacesTheMarks()
    {
        var vm = Browser();
        vm.SetProjectConversations(new HashSet<string> { "eder" });
        vm.SetProjectConversations(new HashSet<string> { "aloth" });

        Assert.False(Item(vm, "eder").IsEdited);
        Assert.True(Item(vm, "aloth").IsEdited);
    }

    [Fact]
    public void NoProject_ClearsMarks_HidesTheCheckbox_AndTurnsItOff()
    {
        var vm = Browser();
        vm.SetProjectConversations(new HashSet<string> { "eder" });
        vm.OnlyProjectConversations = true;

        vm.SetProjectConversations(null);

        Assert.All(AllItems(vm), i => Assert.False(i.IsEdited));
        Assert.False(vm.HasProject);
        Assert.False(vm.OnlyProjectConversations);
    }

    [Fact]
    public void OnlyProjectConversations_KeepsEditedConversations_InExpandedFolders()
    {
        var vm = Browser();
        vm.SetProjectConversations(new HashSet<string> { "eder_poi", "gather_your_party" });

        vm.OnlyProjectConversations = true;

        var roots = vm.FilteredRoots.ToList();
        var companions = Assert.IsType<ConversationFolderViewModel>(roots[0]);
        Assert.True(companions.IsExpanded);
        Assert.Empty(companions.Items);                      // eder and aloth are untouched
        var poi = Assert.Single(companions.Subfolders);
        Assert.True(poi.IsExpanded);
        Assert.Equal("eder_poi", Assert.Single(poi.Items).Name);
        Assert.Equal("gather_your_party", Assert.IsType<ConversationItemViewModel>(roots[1]).Name);
        Assert.Equal(2, roots.Count);
    }

    [Fact]
    public void OnlyProjectConversations_KeepsNewConversations()
    {
        var vm = Browser();
        var newFolder = new ConversationFolderViewModel(string.Empty, isExpanded: true, displayName: "New conversations");
        newFolder.Items.Add(new ConversationItemViewModel(FileIn("", "my_new"), isNew: true));
        vm.Folders.Insert(0, newFolder);
        vm.SetProjectConversations(new HashSet<string>());

        vm.OnlyProjectConversations = true;

        var kept = Assert.Single(vm.FilteredFolders);
        Assert.Equal("my_new", Assert.Single(kept.Items).Name);
    }

    [Fact]
    public void Load_AfterTheProjectIsKnown_KeepsTheMarks()
    {
        // A game folder can load after the project is open (MainWindowViewModel loads the
        // browser then); the fresh items must still show the project's marks.
        var vm = new GameBrowserViewModel(new StubDispatcher());
        vm.SetProjectConversations(new HashSet<string> { "scene" });

        vm.Load(new FakeGameDataProvider("test-game", "en",
            new DialogEditor.Core.Models.Conversation("scene", [], new DialogEditor.Core.Models.StringTable([])),
            new DialogEditor.Core.Models.Conversation("other", [], new DialogEditor.Core.Models.StringTable([]))));

        Assert.True(Item(vm, "scene").IsEdited);
        Assert.False(Item(vm, "other").IsEdited);
    }

    [Fact]
    public void OnlyProjectConversations_CombinesWithTheTextFilter()
    {
        var vm = Browser();
        vm.SetProjectConversations(new HashSet<string> { "eder", "aloth" });
        vm.OnlyProjectConversations = true;
        vm.FilterText = "aloth";

        var companions = Assert.Single(vm.FilteredFolders);
        Assert.Equal("aloth", Assert.Single(companions.Items).Name);
    }
}

/// <summary>Issue 85, part C: the main window tells the dock which conversations the project changes.</summary>
public class MainWindowProjectMarksTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowProjectMarksTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_marks_{Guid.NewGuid():N}.json");
        _projectPath  = Path.Combine(Path.GetTempPath(), $"marks_{Guid.NewGuid():N}.dialogproject");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
        try { File.Delete(_projectPath);  } catch { /* best-effort */ }
    }

    private static ConversationPatch Patch(string name, bool empty) =>
        new ConversationPatch(name, ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            NodeComments = empty ? new Dictionary<int, string>() : new Dictionary<int, string> { [1] = "note" },
        };

    [Fact]
    public void OpeningAProject_MarksConversationsWithANonEmptyPatch_AndClosingClearsThem()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        var folder = new ConversationFolderViewModel("companions");
        ConversationItemViewModel Add(string name)
        {
            var item = new ConversationItemViewModel(new ConversationFile(name, "companions",
                $@"companions\{name}.conversation", $@"companions\{name}.stringtable"));
            folder.Items.Add(item);
            return item;
        }
        var edited = Add("edited");
        var opened = Add("opened_only");
        var other  = Add("other");
        vm.Browser.Folders.Add(folder);

        DialogProjectSerializer.SaveToFile(_projectPath, DialogProject.Empty("marks")
            .WithPatch(Patch("edited", empty: false))
            .WithPatch(Patch("opened_only", empty: true)));
        vm.OpenRecentProjectCommand.Execute(Path.GetFullPath(_projectPath));

        Assert.True(edited.IsEdited);
        Assert.False(opened.IsEdited);   // an empty patch is not an edit
        Assert.False(other.IsEdited);
        Assert.True(vm.Browser.HasProject);

        vm.CloseProjectCommand.Execute(null);

        Assert.False(edited.IsEdited);
        Assert.False(vm.Browser.HasProject);
    }
}
