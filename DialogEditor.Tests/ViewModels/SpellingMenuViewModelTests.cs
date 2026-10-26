using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// GitHub issue 313: right-clicking a misspelt word in Node Details offers suggestions and
/// "add to dictionary". The view asks the view model what to offer for a text position.
public class SpellingMenuViewModelTests : IDisposable
{
    private readonly string _spellDir;

    public SpellingMenuViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
        _spellDir = Path.Combine(Path.GetTempPath(), $"spellmenu_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_spellDir);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DialogEditor.slnx")))
            root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(_spellDir, "en_US.aff"));
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(_spellDir, "en_US.dic"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_spellDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private SpellDictionaryStore NewStore() => new(_spellDir, Path.Combine(_spellDir, "user.txt"));

    private NodeDetailViewModel Vm(SpellDictionaryStore? store = null)
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2", ActiveLanguage = "en" };
        vm.SpellChecker = new SpellCheckService(store ?? NewStore());
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
            Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);
        vm.Load(new NodeViewModel(node, new StringEntry(1, "cat", "")));
        return vm;
    }

    [Fact]
    public void NoChecker_NoMenu()
        => Assert.Null(new NodeDetailViewModel().SpellingMenuFinder);

    [Fact]
    public void InsideAMisspeltWord_OffersItsSuggestions()
    {
        var menu = Vm().SpellingMenuFinder!("The captian glows", 6);
        Assert.NotNull(menu);
        Assert.Equal("captian", menu!.Word);
        Assert.Equal(4, menu.Start);
        Assert.Equal(7, menu.Length);
        Assert.Contains("captain", menu.Suggestions);
        Assert.InRange(menu.Suggestions.Count, 1, SpellingMenu.MaxSuggestions);
    }

    [Fact]
    public void CaretRightAfterTheWord_CountsAsOnIt()
        => Assert.Equal("captian", Vm().SpellingMenuFinder!("The captian glows", 11)!.Word);

    [Fact]
    public void CaretRightBeforeTheWord_CountsAsOnIt()
        => Assert.Equal("captian", Vm().SpellingMenuFinder!("The captian glows", 4)!.Word);

    [Fact]
    public void OnACorrectWord_NoMenu()
        => Assert.Null(Vm().SpellingMenuFinder!("The captian glows", 14));

    [Fact]
    public void MaxSuggestions_IsFive()
        => Assert.Equal(5, SpellingMenu.MaxSuggestions);

    [Fact]
    public void AddToDictionary_NotWired_IsNull()
        => Assert.Null(Vm().SpellingMenuFinder!("captian", 0)!.AddToDictionary);

    [Fact]
    public void AddToDictionary_AddsTheWord_AndRefreshesTheSquiggles()
    {
        var store = NewStore();
        var vm = Vm(store);
        vm.AddWordToDictionary = store.AddWord;
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SpellingMenuFinder!("captian", 0)!.AddToDictionary!();

        Assert.Contains(nameof(NodeDetailViewModel.SpellSpanFinder), raised);
        Assert.Null(vm.SpellingMenuFinder!("captian", 0));
    }

    [Fact]
    public void AddToProjectDictionary_OnlyWithAProject()
    {
        var vm = Vm();
        Assert.Null(vm.SpellingMenuFinder!("captian", 0)!.AddToProjectDictionary);

        var added = new List<string>();
        vm.AddWordToProjectDictionary = added.Add;
        vm.SpellingMenuFinder!("captian", 0)!.AddToProjectDictionary!();
        Assert.Equal(["captian"], added);
    }
}
