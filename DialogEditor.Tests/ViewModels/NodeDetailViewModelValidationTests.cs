using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelValidationTests : IDisposable
{
    private readonly string _spellDir;

    public NodeDetailViewModelValidationTests()
    {
        Loc.Configure(new StubStringProvider());
        _spellDir = Path.Combine(Path.GetTempPath(), $"ndspell_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_spellDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_spellDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    /// A SpellCheckService over the fixture .aff/.dic (words: cat/ship/captain/glows + S rule).
    private SpellCheckService FixtureChecker()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DialogEditor.slnx")))
            root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(_spellDir, "en_US.aff"));
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(_spellDir, "en_US.dic"));
        return new SpellCheckService(
            new SpellDictionaryStore(_spellDir, Path.Combine(_spellDir, "user.txt")));
    }

    // Build a NodeViewModel with the given Default/Female text (mirrors the
    // construction in NodeDetailViewModelTests.MakeNode).
    private static NodeViewModel MakeNode(string defaultText, string femaleText = "")
    {
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
            Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);
        return new NodeViewModel(node, new StringEntry(1, defaultText, femaleText));
    }

    private static NodeDetailViewModel Loaded(string defaultText, string femaleText = "")
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2" };
        vm.Load(MakeNode(defaultText, femaleText));
        return vm;
    }

    [Fact]
    public void TokenWarnings_CleanText_IsEmpty()
        => Assert.Empty(Loaded("Hello [Player Name].").TokenWarnings);

    [Fact]
    public void TokenWarnings_MisspelledToken_UsesSuggestionBranch()
    {
        var msg = Assert.Single(Loaded("Hi [Player Nmae]!").TokenWarnings);
        // The stub echoes resource keys, so the message IS the key of the branch taken.
        Assert.Equal("Validation_UnknownToken_Suggest", msg);
    }

    [Fact]
    public void TokenWarnings_RecomputeOnDefaultTextEdit()
    {
        var vm = Loaded("clean text");
        Assert.Empty(vm.TokenWarnings);
        vm.DefaultText = "now with [Player Nmae]";
        Assert.NotEmpty(vm.TokenWarnings);
    }

    [Fact]
    public void TokenWarnings_ValidatesFemaleText()
        => Assert.NotEmpty(Loaded("clean", femaleText: "she says [Player Nmae]").TokenWarnings);

    // ── Spelling rows (spell checker feature; issue 9 adds the quick fixes) ──────

    private NodeDetailViewModel Checked(string defaultText, string femaleText = "")
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2", ActiveLanguage = "en" };
        vm.SpellChecker = FixtureChecker();
        vm.Load(MakeNode(defaultText, femaleText));
        return vm;
    }

    [Fact]
    public void SpellingIssues_Misspelling_AppearsWithChecker()
    {
        var issue = Assert.Single(Checked("captian glows").SpellingIssues);
        Assert.Equal("captian", issue.Word);
        Assert.Equal("captain", issue.Suggestion);
        Assert.Equal("Spelling_Misspelled_Suggest", issue.Message);
        Assert.Equal("Spelling_UseSuggestion", issue.ApplyLabel);
    }

    [Fact]
    public void TokenWarnings_NoLongerCarrySpelling()
    {
        var vm = Checked("captian glows [Player Nmae]");
        Assert.Equal(["Validation_UnknownToken_Suggest"], vm.TokenWarnings);
        Assert.Single(vm.SpellingIssues);
    }

    [Fact]
    public void SpellingIssues_NullChecker_IsEmpty()
        => Assert.Empty(Loaded("captian glows").SpellingIssues);

    [Fact]
    public void SpellingIssues_RecomputeOnEdit()
    {
        var vm = Checked("captain");
        Assert.Empty(vm.SpellingIssues);
        vm.DefaultText = "captian";
        Assert.Single(vm.SpellingIssues);
    }

    // Issue 9: accept the proposed word with one click.
    [Fact]
    public void ApplySuggestion_ReplacesTheWordInDefaultText()
    {
        var vm = Checked("captian glows");
        var issue = Assert.Single(vm.SpellingIssues);
        Assert.True(issue.CanApplySuggestion);

        issue.ApplySuggestionCommand.Execute(null);

        Assert.Equal("captain glows", vm.DefaultText);
        Assert.Empty(vm.SpellingIssues);
    }

    [Fact]
    public void ApplySuggestion_ReplacesTheWordInFemaleText()
    {
        var vm = Checked("captain glows", femaleText: "captian glows");
        Assert.Single(vm.SpellingIssues).ApplySuggestionCommand.Execute(null);
        Assert.Equal("captain glows", vm.FemaleText);
        Assert.Equal("captain glows", vm.DefaultText);
    }

    [Fact]
    public void ApplySuggestion_FixesEveryOccurrenceOfTheWord_ButNotLongerWords()
    {
        // The checker reports each misspelled word once, so its fix covers every occurrence;
        // "captians" is a different word and is left alone.
        var vm = Checked("captian ship captian captians");
        vm.SpellingIssues.Single(i => i.Word == "captian").ApplySuggestionCommand.Execute(null);
        Assert.Equal("captain ship captain captians", vm.DefaultText);
    }

    [Fact]
    public void ApplySuggestion_NoSuggestion_CannotApply()
    {
        var issue = Assert.Single(Checked("zzqx").SpellingIssues);
        Assert.Null(issue.Suggestion);
        Assert.False(issue.CanApplySuggestion);
    }

    // Issue 9: add a word to the user dictionary straight from the detail panel.
    [Fact]
    public void AddToDictionary_AddsTheWord_AndTheWarningGoes()
    {
        var vm = Checked("Harbourmaster glows");
        var added = new List<string>();
        var store = new SpellDictionaryStore(_spellDir, Path.Combine(_spellDir, "user.txt"));
        vm.AddWordToDictionary = w => { added.Add(w); store.AddWord(w); };
        vm.SpellChecker = new SpellCheckService(store);
        vm.Load(MakeNode("Harbourmaster glows"));
        var issue = Assert.Single(vm.SpellingIssues);
        Assert.True(issue.CanAddToDictionary);

        issue.AddToDictionaryCommand.Execute(null);

        Assert.Equal(["Harbourmaster"], added);
        Assert.Empty(vm.SpellingIssues);
    }

    [Fact]
    public void AddToDictionary_NotWired_CannotAdd()
        => Assert.False(Assert.Single(Checked("captian glows").SpellingIssues).CanAddToDictionary);

    // ── Inline squiggles (issue 9) ───────────────────────────────────────────

    [Fact]
    public void SpellSpanFinder_NoChecker_IsNull()
        => Assert.Null(Loaded("captian glows").SpellSpanFinder);

    [Fact]
    public void SpellSpanFinder_ChecksTheGivenText()
    {
        // The view passes the TextBox's live text, which runs ahead of DefaultText while typing.
        var finder = Checked("cat").SpellSpanFinder!;
        Assert.Equal([new SpellSpan(0, 7)], finder("captian glows"));
    }

    [Fact]
    public void AddToDictionary_RaisesANewFinder_WithoutTheWord()
    {
        var vm = Checked("Harbourmaster glows");
        var store = new SpellDictionaryStore(_spellDir, Path.Combine(_spellDir, "user.txt"));
        vm.AddWordToDictionary = store.AddWord;
        vm.SpellChecker = new SpellCheckService(store);
        vm.Load(MakeNode("Harbourmaster glows"));
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Single(vm.SpellingIssues).AddToDictionaryCommand.Execute(null);

        Assert.Contains(nameof(NodeDetailViewModel.SpellSpanFinder), raised);
        Assert.Empty(vm.SpellSpanFinder!("Harbourmaster glows"));
    }

    [Fact]
    public void ActiveLanguageChange_RaisesSpellSpanFinder()
    {
        var vm = Checked("captian");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.ActiveLanguage = "fr";
        Assert.Contains(nameof(NodeDetailViewModel.SpellSpanFinder), raised);
        Assert.Empty(vm.SpellSpanFinder!("captian"));   // no fr dictionary installed
    }
}
